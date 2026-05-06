using System;
using System.Collections.Generic;

namespace Structura.Generator;

// ── Value discriminated union ────────────────────────────────────────────────

internal abstract class GenValue { }
internal sealed class GenStringValue : GenValue { }
internal sealed class GenNullValue : GenValue { }

/// <summary>Number literal with no decimal point → maps to <c>long</c>.</summary>
internal sealed class GenLongValue : GenValue { }

/// <summary>Number literal that contains a decimal point → maps to <c>decimal</c>.</summary>
internal sealed class GenDecimalValue : GenValue { }

internal sealed class GenBoolValue : GenValue
{
    public bool Value { get; }
    public GenBoolValue(bool value)
    {
        Value = value;
    }
}

/// <summary>Nested object — skipped by the emitter in V1.</summary>
internal sealed class GenObjectValue : GenValue { }

/// <summary>Array — skipped by the emitter in V1.</summary>
internal sealed class GenArrayValue : GenValue { }

// ── Property ─────────────────────────────────────────────────────────────────

internal sealed class GenProperty
{
    public GenProperty(string name, GenValue value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public GenValue Value { get; }
}

// ── Parser ───────────────────────────────────────────────────────────────────

/// <summary>
/// Minimal, allocation-light structural JSON parser for netstandard2.0.
/// Reads only the root-object's direct properties; nested objects and arrays
/// are skipped over without deep parsing. On any error the method returns an
/// empty list so the generator emits nothing rather than crashing the build.
/// </summary>
internal static class GeneratorJsonParser
{
    public static List<GenProperty> ParseRootProperties(string json)
    {
        var result = new List<GenProperty>();
        try
        {
            var p = 0;
            SkipWhitespace(json, ref p);
            if (p >= json.Length || json[p] != '{')
            {
                return result;
            }

            p++; // consume '{'
            SkipWhitespace(json, ref p);

            if (p < json.Length && json[p] == '}')
            {
                return result; // empty object
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

                GenValue value = ClassifyValue(json, ref p);
                result.Add(new GenProperty(key, value));

                SkipWhitespace(json, ref p);
                if (p >= json.Length)
                {
                    break;
                }

                if (json[p] == ',')
                {
                    p++; // consume ','
                    continue;
                }

                if (json[p] == '}')
                {
                    break;
                }

                break; // unexpected character
            }
        }
        catch
        {
            // Swallow all parse errors — generator emits nothing.
            result.Clear();
        }

        return result;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

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

    private static GenValue ClassifyValue(string json, ref int p)
    {
        if (p >= json.Length)
        {
            throw new InvalidOperationException("Unexpected end of JSON.");
        }

        char c = json[p];

        switch (c)
        {
            case '"':
                SkipStringLiteral(json, ref p);
                return new GenStringValue();

            case 'n':
                ConsumeToken(json, ref p, "null");
                return new GenNullValue();

            case 't':
                ConsumeToken(json, ref p, "true");
                return new GenBoolValue(true);

            case 'f':
                ConsumeToken(json, ref p, "false");
                return new GenBoolValue(false);

            case '{':
                SkipBalanced(json, ref p, '{', '}');
                return new GenObjectValue();

            case '[':
                SkipBalanced(json, ref p, '[', ']');
                return new GenArrayValue();

            default:
                if (c == '-' || char.IsDigit(c))
                {
                    return ReadNumber(json, ref p);
                }

                throw new InvalidOperationException($"Unexpected character '{c}' at {p}.");
        }
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

    private static GenValue ReadNumber(string json, ref int p)
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
            hasDecimalPoint = true; // treat scientific notation as decimal
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

        return hasDecimalPoint ? (GenValue)new GenDecimalValue() : new GenLongValue();
    }
}
