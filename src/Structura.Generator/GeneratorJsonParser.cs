using System;
using System.Collections.Generic;

namespace Structura.Generator;

/// <summary>
/// Recursive structural JSON parser for netstandard2.0. Builds a
/// <see cref="JsonRootInfo"/> schema from a sample document: scalars, nested
/// objects, primitive and object array collections (with field-union merging
/// across observations and intersection-based RequiredKeys), empty and
/// heterogeneous arrays. Returns null on any parse failure so the generator
/// emits nothing rather than crashing the build.
/// </summary>
internal static class GeneratorJsonParser
{
    public static JsonRootInfo? ParseRootInfo(string json)
    {
        try
        {
            var p = 0;
            SkipWhitespace(json, ref p);
            if (p >= json.Length || json[p] != '{')
            {
                return null;
            }

            JsonGenObject root = ParseObjectInfo(json, ref p, typeName: "Root");
            return new JsonRootInfo(root);
        }
        catch
        {
            return null;
        }
    }

    private static JsonGenObject ParseObjectInfo(string json, ref int p, string typeName)
    {
        var scalars = new List<JsonGenProperty>();
        var nestedObjects = new List<JsonGenNestedObject>();
        var collections = new List<JsonGenCollection>();
        var requiredKeys = new HashSet<string>(StringComparer.Ordinal);

        p++; // consume '{'
        SkipWhitespace(json, ref p);

        if (p < json.Length && json[p] == '}')
        {
            p++;
            return new JsonGenObject(typeName, scalars, nestedObjects, collections, requiredKeys);
        }

        while (p < json.Length)
        {
            SkipWhitespace(json, ref p);
            if (p >= json.Length || json[p] != '"')
            {
                break;
            }

            string key = ReadString(json, ref p);
            SkipWhitespace(json, ref p);

            if (p >= json.Length || json[p] != ':')
            {
                break;
            }

            p++; // consume ':'
            SkipWhitespace(json, ref p);

            if (p < json.Length && json[p] == '{')
            {
                string nestedTypeName = IdentifierSanitizer.ToPascalCase(key);
                JsonGenObject nested = ParseObjectInfo(json, ref p, nestedTypeName);
                nestedObjects.Add(new JsonGenNestedObject(key, nested));
                requiredKeys.Add(key);
            }
            else if (p < json.Length && json[p] == '[')
            {
                JsonGenCollection coll = ParseArrayInfo(json, ref p, key);
                collections.Add(coll);
                requiredKeys.Add(key);
            }
            else
            {
                JsonGenScalarKind? scalarKind = TryClassifyScalar(json, ref p);
                if (scalarKind.HasValue)
                {
                    scalars.Add(new JsonGenProperty(key, scalarKind.Value));
                    requiredKeys.Add(key);
                }
            }

            SkipWhitespace(json, ref p);
            if (p >= json.Length)
            {
                break;
            }

            if (json[p] == ',')
            {
                p++;
                continue;
            }

            if (json[p] == '}')
            {
                p++;
                break;
            }

            break;
        }

        return new JsonGenObject(typeName, scalars, nestedObjects, collections, requiredKeys);
    }

    private static JsonGenCollection ParseArrayInfo(string json, ref int p, string key)
    {
        p++; // consume '['
        SkipWhitespace(json, ref p);

        if (p < json.Length && json[p] == ']')
        {
            p++;
            return new JsonGenCollection(key, JsonGenItemKind.Empty, objectItem: null, primitiveItemKind: null);
        }

        var observedObjects = new List<JsonGenObject>();
        JsonGenScalarKind? observedPrimitiveKind = null;
        var hasPrimitive = false;
        var heterogeneous = false;
        string itemTypeName = IdentifierSanitizer.ToPascalCase(key);

        while (p < json.Length)
        {
            SkipWhitespace(json, ref p);
            if (p >= json.Length)
            {
                break;
            }

            char c = json[p];

            if (c == '{')
            {
                JsonGenObject item = ParseObjectInfo(json, ref p, itemTypeName);
                observedObjects.Add(item);
                if (hasPrimitive)
                {
                    heterogeneous = true;
                }
            }
            else if (c == '[')
            {
                // Arrays-of-arrays — out of scope for V1.
                SkipBalanced(json, ref p, '[', ']');
                heterogeneous = true;
            }
            else
            {
                JsonGenScalarKind? itemKind = TryClassifyScalar(json, ref p);
                if (!itemKind.HasValue)
                {
                    heterogeneous = true;
                }
                else
                {
                    if (observedObjects.Count > 0)
                    {
                        heterogeneous = true;
                    }

                    if (!observedPrimitiveKind.HasValue)
                    {
                        observedPrimitiveKind = itemKind.Value;
                    }
                    else if (!TryMergePrimitiveKinds(observedPrimitiveKind.Value, itemKind.Value, out JsonGenScalarKind merged))
                    {
                        heterogeneous = true;
                    }
                    else
                    {
                        observedPrimitiveKind = merged;
                    }

                    hasPrimitive = true;
                }
            }

            SkipWhitespace(json, ref p);
            if (p >= json.Length)
            {
                break;
            }

            if (json[p] == ',')
            {
                p++;
                continue;
            }

            if (json[p] == ']')
            {
                p++;
                break;
            }

            break;
        }

        if (heterogeneous)
        {
            return new JsonGenCollection(key, JsonGenItemKind.Heterogeneous, objectItem: null, primitiveItemKind: null);
        }

        if (hasPrimitive)
        {
            return new JsonGenCollection(key, JsonGenItemKind.Primitive, objectItem: null, primitiveItemKind: observedPrimitiveKind);
        }

        if (observedObjects.Count > 0)
        {
            JsonGenObject union = MergeObjectObservations(observedObjects, itemTypeName);
            return new JsonGenCollection(key, JsonGenItemKind.Object, objectItem: union, primitiveItemKind: null);
        }

        return new JsonGenCollection(key, JsonGenItemKind.Empty, objectItem: null, primitiveItemKind: null);
    }

    /// <summary>
    /// Merges multiple object observations into a union shape: scalars unified
    /// by name (with kind merging), nested objects and collections collected
    /// by first occurrence, RequiredKeys = intersection of every observation.
    /// </summary>
    private static JsonGenObject MergeObjectObservations(List<JsonGenObject> observations, string typeName)
    {
        var scalarKinds = new Dictionary<string, JsonGenScalarKind>(StringComparer.Ordinal);
        var conflictedScalarKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonGenObject obs in observations)
        {
            foreach (JsonGenProperty prop in obs.Scalars)
            {
                if (!scalarKinds.TryGetValue(prop.Name, out JsonGenScalarKind existing))
                {
                    scalarKinds[prop.Name] = prop.Kind;
                }
                else if (TryMergeScalarKinds(existing, prop.Kind, out JsonGenScalarKind merged))
                {
                    scalarKinds[prop.Name] = merged;
                }
                else
                {
                    conflictedScalarKeys.Add(prop.Name);
                }
            }
        }

        var scalars = new List<JsonGenProperty>();
        foreach (KeyValuePair<string, JsonGenScalarKind> kv in scalarKinds)
        {
            if (conflictedScalarKeys.Contains(kv.Key))
            {
                continue;
            }
            scalars.Add(new JsonGenProperty(kv.Key, kv.Value));
        }

        var nestedObjects = new List<JsonGenNestedObject>();
        var collections = new List<JsonGenCollection>();
        var nestedObsByName = new Dictionary<string, List<JsonGenObject>>(StringComparer.Ordinal);
        var nestedKeyOrder = new List<string>();
        var seenCollectionKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonGenObject obs in observations)
        {
            foreach (JsonGenNestedObject n in obs.NestedObjects)
            {
                if (!nestedObsByName.TryGetValue(n.Name, out List<JsonGenObject>? bucket))
                {
                    bucket = new List<JsonGenObject>();
                    nestedObsByName.Add(n.Name, bucket);
                    nestedKeyOrder.Add(n.Name);
                }
                bucket.Add(n.Object);
            }
            foreach (JsonGenCollection c in obs.Collections)
            {
                if (seenCollectionKeys.Add(c.Name))
                {
                    collections.Add(c);
                }
            }
        }

        foreach (string nestedKey in nestedKeyOrder)
        {
            List<JsonGenObject> obsList = nestedObsByName[nestedKey];
            string nestedTypeName = IdentifierSanitizer.ToPascalCase(nestedKey);
            JsonGenObject merged = obsList.Count == 1
                ? obsList[0]
                : MergeObjectObservations(obsList, nestedTypeName);
            nestedObjects.Add(new JsonGenNestedObject(nestedKey, merged));
        }

        var requiredKeys = new HashSet<string>(observations[0].RequiredKeys, StringComparer.Ordinal);
        for (var i = 1; i < observations.Count; i++)
        {
            requiredKeys.IntersectWith(observations[i].RequiredKeys);
        }

        return new JsonGenObject(typeName, scalars, nestedObjects, collections, requiredKeys);
    }

    private static bool TryMergeScalarKinds(JsonGenScalarKind a, JsonGenScalarKind b, out JsonGenScalarKind merged)
    {
        if (a == b)
        {
            merged = a;
            return true;
        }

        // String + NullableString in either order → NullableString.
        if ((a == JsonGenScalarKind.String && b == JsonGenScalarKind.NullableString) ||
            (a == JsonGenScalarKind.NullableString && b == JsonGenScalarKind.String))
        {
            merged = JsonGenScalarKind.NullableString;
            return true;
        }

        merged = default;
        return false;
    }

    private static bool TryMergePrimitiveKinds(JsonGenScalarKind a, JsonGenScalarKind b, out JsonGenScalarKind merged)
    {
        return TryMergeScalarKinds(a, b, out merged);
    }

    private static JsonGenScalarKind? TryClassifyScalar(string json, ref int p)
    {
        if (p >= json.Length)
        {
            return null;
        }

        char c = json[p];

        switch (c)
        {
            case '"':
                SkipStringLiteral(json, ref p);
                return JsonGenScalarKind.String;

            case 'n':
                ConsumeToken(json, ref p, "null");
                return JsonGenScalarKind.NullableString;

            case 't':
                ConsumeToken(json, ref p, "true");
                return JsonGenScalarKind.Boolean;

            case 'f':
                ConsumeToken(json, ref p, "false");
                return JsonGenScalarKind.Boolean;

            default:
                if (c == '-' || char.IsDigit(c))
                {
                    return ReadNumberKind(json, ref p);
                }
                return null;
        }
    }

    private static JsonGenScalarKind ReadNumberKind(string json, ref int p)
    {
        var hasDecimalPoint = false;

        if (json[p] == '-')
        {
            p++;
        }

        while (p < json.Length && char.IsDigit(json[p]))
        {
            p++;
        }

        if (p < json.Length && json[p] == '.')
        {
            hasDecimalPoint = true;
            p++;
            while (p < json.Length && char.IsDigit(json[p]))
            {
                p++;
            }
        }

        if (p < json.Length && (json[p] == 'e' || json[p] == 'E'))
        {
            hasDecimalPoint = true;
            p++;
            if (p < json.Length && (json[p] == '+' || json[p] == '-'))
            {
                p++;
            }
            while (p < json.Length && char.IsDigit(json[p]))
            {
                p++;
            }
        }

        return hasDecimalPoint ? JsonGenScalarKind.Decimal : JsonGenScalarKind.Long;
    }

    // ── Low-level helpers ────────────────────────────────────────────────────

    private static void SkipWhitespace(string json, ref int p)
    {
        while (p < json.Length)
        {
            char c = json[p];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                p++;
            }
            else
            {
                return;
            }
        }
    }

    /// <summary>
    /// Reads a JSON string literal starting at <c>json[p]</c> (which must be
    /// <c>"</c>) and returns the raw content (escape sequences are NOT decoded —
    /// we only need the key name for identity, and sample keys are plain ASCII).
    /// </summary>
    private static string ReadString(string json, ref int p)
    {
        p++; // consume opening '"'
        int start = p;

        while (p < json.Length)
        {
            char c = json[p];
            if (c == '\\')
            {
                p += 2; // skip escape
                continue;
            }

            if (c == '"')
            {
                string s = json.Substring(start, p - start);
                p++; // consume closing '"'
                return s;
            }

            p++;
        }

        throw new InvalidOperationException("Unterminated string in sample JSON.");
    }

    private static void SkipStringLiteral(string json, ref int p)
    {
        p++; // consume opening '"'

        while (p < json.Length)
        {
            char c = json[p];
            if (c == '\\')
            {
                p += 2;
                continue;
            }

            if (c == '"')
            {
                p++;
                return;
            }

            p++;
        }
    }

    private static void ConsumeToken(string json, ref int p, string token)
    {
        for (var i = 0; i < token.Length; i++)
        {
            if (p >= json.Length || json[p] != token[i])
            {
                throw new InvalidOperationException($"Expected '{token}' at {p}.");
            }

            p++;
        }
    }

    private static void SkipBalanced(string json, ref int p, char open, char close)
    {
        if (p >= json.Length || json[p] != open)
        {
            return;
        }

        p++; // consume open
        var depth = 1;

        while (p < json.Length && depth > 0)
        {
            char c = json[p];

            if (c == '"')
            {
                SkipStringLiteral(json, ref p);
                continue;
            }

            if (c == open)
            {
                depth++;
            }
            else if (c == close)
            {
                depth--;
            }

            p++;
        }
    }

}
