using System;
using System.Collections.Generic;
using System.Globalization;

namespace Structura.Generator;

// ── Parser ───────────────────────────────────────────────────────────────────

/// <summary>
/// Minimal structural XML scanner that runs inside the source generator
/// (netstandard2.0 — cannot depend on <c>Structura.Runtime.Xml</c>). It
/// determines the document's effective data root (descending through
/// single-element envelope wrappers) and recursively classifies each
/// element's children into scalars, wrapper / sibling-group / pure-text-leaf
/// collections, or unhandled-structural skips. Returns <see langword="null"/>
/// only when the file is so malformed that even the literal root can't be
/// parsed; in every other case the caller gets a usable
/// <see cref="XmlRootInfo"/> plus an <see cref="XmlGenObservations"/> log
/// describing what V1 dropped.
/// </summary>
internal static class GeneratorXmlParser
{
    public static XmlRootInfo? ParseRootInfo(string xml)
    {
        var obs = new XmlGenObservations();
        try
        {
            var p = 0;
            SkipProlog(xml, ref p, obs);

            ElementInfo? literalRoot = TryParseElementAt(xml, ref p, obs);
            if (literalRoot == null)
            {
                return null;
            }

            // The literal root must be the only top-level element.
            SkipTrailing(xml, ref p, obs);
            if (p < xml.Length)
            {
                return null;
            }

            // Walk the wrapper chain. The rule fires only when:
            //   - 0 (non-xmlns) attributes, AND
            //   - 0 pure-text scalar children, AND
            //   - exactly 1 structural element child.
            var wrapperChain = new List<string>();
            ElementInfo current = literalRoot;
            while (NonXmlnsAttributeCount(current.Attributes) == 0
                && current.PureTextChildren.Count == 0
                && current.StructuralChildren.Count == 1)
            {
                int childStart = current.StructuralChildren[0].Position;
                ElementInfo? next = TryParseElementAt(xml, ref childStart, obs);
                if (next == null)
                {
                    break;
                }
                wrapperChain.Add(next.Name);
                current = next;
            }

            string rootTypeName = ClassNameDeriver.Derive(literalRoot.Name)
                + "Root"; // placeholder; real type name comes from the file name in the emitter
            (List<XmlGenProperty> scalars,
             List<XmlGenCollection> collections,
             List<XmlGenNestedObject> nestedObjects) =
                ClassifyElementContents(xml, current, parentTypeName: current.Name, obs);

            return new XmlRootInfo(
                literalRoot.Name, wrapperChain, scalars, collections, nestedObjects, obs);
        }
        catch
        {
            return null;
        }
    }

    // ── Classification (recursive) ────────────────────────────────────────────

    private static (
        List<XmlGenProperty> scalars,
        List<XmlGenCollection> collections,
        List<XmlGenNestedObject> nestedObjects)
        ClassifyElementContents(
            string xml,
            ElementInfo info,
            string parentTypeName,
            XmlGenObservations obs)
    {
        var scalars = new List<XmlGenProperty>();
        var collections = new List<XmlGenCollection>();
        var nestedObjects = new List<XmlGenNestedObject>();

        // 1. Attributes (xmlns* filtered out, observation flag set).
        foreach (XmlGenProperty attr in info.Attributes)
        {
            if (IsXmlnsAttribute(attr.Name))
            {
                obs.SawNamespaceDecl = true;
                continue;
            }
            scalars.Add(attr);
        }

        // 2. Pure-text element children → scalars.
        scalars.AddRange(info.PureTextChildren);

        // 3. Structural children: classify by name groups.
        var nameToOccurrences = new Dictionary<string, List<ChildRef>>(StringComparer.Ordinal);
        foreach (ChildRef child in info.StructuralChildren)
        {
            if (!nameToOccurrences.TryGetValue(child.Name, out List<ChildRef>? list))
            {
                list = new List<ChildRef>();
                nameToOccurrences[child.Name] = list;
            }
            list.Add(child);
        }

        foreach (KeyValuePair<string, List<ChildRef>> entry in nameToOccurrences)
        {
            string name = entry.Key;
            List<ChildRef> occurrences = entry.Value;

            if (occurrences.Count == 1)
            {
                // Single occurrence → first try Shape 1/2 wrapper, then nested
                // object (Step 10), else residual STR0009.
                XmlGenCollection? wrapper = TryClassifyAsWrapper(xml, occurrences[0], obs);
                if (wrapper != null)
                {
                    collections.Add(wrapper);
                    continue;
                }

                XmlGenNestedObject? nested = TryClassifyAsNestedObject(xml, occurrences[0], obs);
                if (nested != null)
                {
                    nestedObjects.Add(nested);
                    continue;
                }

                obs.SkippedStructural.Add((parentTypeName, name));
            }
            else
            {
                // Multiple occurrences → Shape 3 sibling group.
                XmlGenCollection? sibling = TryClassifyAsSiblingGroup(xml, name, occurrences, obs);
                if (sibling != null)
                {
                    collections.Add(sibling);
                }
                else
                {
                    obs.SkippedStructural.Add((parentTypeName, name));
                }
            }
        }

        return (scalars, collections, nestedObjects);
    }

    /// <summary>
    /// Classifies a single-occurrence structural element as a typed nested
    /// object: zero non-xmlns attributes and at least one child (otherwise
    /// the caller falls back to STR0009). The body is built by the same
    /// <see cref="ClassifyElementContents"/> path used for collection items
    /// and the root, which gives us recursive nesting for free.
    /// </summary>
    private static XmlGenNestedObject? TryClassifyAsNestedObject(
        string xml,
        ChildRef childRef,
        XmlGenObservations obs)
    {
        int p = childRef.Position;
        ElementInfo? info = TryParseElementAt(xml, ref p, obs);
        if (info == null)
        {
            return null;
        }

        if (NonXmlnsAttributeCount(info.Attributes) != 0)
        {
            // Element carries attributes alongside its structural payload —
            // that's text+attribute / mixed-content territory, deferred to
            // Step 11. The caller surfaces this as STR0009.
            return null;
        }

        // Empty structural element (no attrs, no children). Insertion of a
        // body is not modelled in V1, so we defer to STR0009.
        if (info.PureTextChildren.Count == 0 && info.StructuralChildren.Count == 0)
        {
            return null;
        }

        string typeName = IdentifierSanitizer.ToPascalCase(info.Name) + "Type";
        (List<XmlGenProperty> scalars,
         List<XmlGenCollection> collections,
         List<XmlGenNestedObject> nestedObjects) =
            ClassifyElementContents(xml, info, parentTypeName: typeName, obs);

        var body = new ItemTypeInfo(
            typeName: typeName,
            xmlElementName: info.Name,
            scalars: scalars,
            collections: collections,
            nestedObjects: nestedObjects);

        return new XmlGenNestedObject(info.Name, body);
    }

    private static XmlGenCollection? TryClassifyAsWrapper(
        string xml,
        ChildRef wrapperChild,
        XmlGenObservations obs)
    {
        int p = wrapperChild.Position;
        ElementInfo? wrapperInfo = TryParseElementAt(xml, ref p, obs);
        if (wrapperInfo == null)
        {
            return null;
        }

        // Wrapper must have zero (non-xmlns) attributes.
        if (NonXmlnsAttributeCount(wrapperInfo.Attributes) != 0)
        {
            return null;
        }

        // Wrapper must have at least one element child and all must share a name.
        int totalElementChildren = wrapperInfo.PureTextChildren.Count + wrapperInfo.StructuralChildren.Count;
        if (totalElementChildren == 0)
        {
            return null;
        }
        string? sharedName = null;
        var allPureText = true;
        foreach (XmlGenProperty pt in wrapperInfo.PureTextChildren)
        {
            if (sharedName == null)
            {
                sharedName = pt.Name;
            }
            else if (!string.Equals(sharedName, pt.Name, StringComparison.Ordinal))
            {
                return null;
            }
        }
        foreach (ChildRef st in wrapperInfo.StructuralChildren)
        {
            allPureText = false;
            if (sharedName == null)
            {
                sharedName = st.Name;
            }
            else if (!string.Equals(sharedName, st.Name, StringComparison.Ordinal))
            {
                return null;
            }
        }
        if (sharedName == null)
        {
            return null;
        }

        string wrapperName = wrapperInfo.Name;
        string itemElementName = sharedName;
        string itemTypeName = ClassNameDeriver.Derive(itemElementName);

        // Decide flat vs nested. Flat when wrapperName == itemName + "s" (case-insensitive).
        bool flat = string.Equals(
            wrapperName,
            itemElementName + "s",
            StringComparison.OrdinalIgnoreCase);

        string csharpPropertyName = flat
            ? Pluralize(itemElementName)
            : ClassNameDeriver.Derive(wrapperName);

        // Pure-text-leaf collection: every child is a pure-text scalar.
        if (allPureText)
        {
            return new XmlGenCollection(
                style: flat ? XmlGenCollectionStyle.Flat : XmlGenCollectionStyle.Wrapper,
                csharpPropertyName: csharpPropertyName,
                itemElementName: itemElementName,
                itemTypeName: "string",
                itemIsPureTextLeaf: true,
                wrapperElementName: wrapperName,
                item: null);
        }

        // Structural items: build the item type by union over all wrapper children.
        var itemElementInfos = new List<ElementInfo>();
        foreach (ChildRef st in wrapperInfo.StructuralChildren)
        {
            int sp = st.Position;
            ElementInfo? ei = TryParseElementAt(xml, ref sp, obs);
            if (ei != null)
            {
                itemElementInfos.Add(ei);
            }
        }
        ItemTypeInfo itemType = BuildItemTypeFromCollection(
            xml,
            itemElementInfos,
            itemTypeName,
            itemElementName,
            obs);

        return new XmlGenCollection(
            style: flat ? XmlGenCollectionStyle.Flat : XmlGenCollectionStyle.Wrapper,
            csharpPropertyName: csharpPropertyName,
            itemElementName: itemElementName,
            itemTypeName: itemTypeName,
            itemIsPureTextLeaf: false,
            wrapperElementName: wrapperName,
            item: itemType);
    }

    private static XmlGenCollection? TryClassifyAsSiblingGroup(
        string xml,
        string sharedName,
        List<ChildRef> occurrences,
        XmlGenObservations obs)
    {
        // Sibling-group items can be pure-text or structural. We re-parse each
        // occurrence to inspect its shape.
        var itemElementInfos = new List<ElementInfo>();
        var allPureText = true;
        foreach (ChildRef occ in occurrences)
        {
            int sp = occ.Position;
            ElementInfo? ei = TryParseElementAt(xml, ref sp, obs);
            if (ei == null)
            {
                return null;
            }
            itemElementInfos.Add(ei);

            bool isLeaf = NonXmlnsAttributeCount(ei.Attributes) == 0
                && ei.StructuralChildren.Count == 0
                && ei.PureTextChildren.Count == 0;
            if (!isLeaf)
            {
                allPureText = false;
            }
        }

        string itemTypeName = ClassNameDeriver.Derive(sharedName);
        string csharpPropertyName = Pluralize(sharedName);

        if (allPureText)
        {
            return new XmlGenCollection(
                style: XmlGenCollectionStyle.SiblingGroup,
                csharpPropertyName: csharpPropertyName,
                itemElementName: sharedName,
                itemTypeName: "string",
                itemIsPureTextLeaf: true,
                wrapperElementName: null,
                item: null);
        }

        ItemTypeInfo itemType = BuildItemTypeFromCollection(
            xml,
            itemElementInfos,
            itemTypeName,
            sharedName,
            obs);

        return new XmlGenCollection(
            style: XmlGenCollectionStyle.SiblingGroup,
            csharpPropertyName: csharpPropertyName,
            itemElementName: sharedName,
            itemTypeName: itemTypeName,
            itemIsPureTextLeaf: false,
            wrapperElementName: null,
            item: itemType);
    }

    private static ItemTypeInfo BuildItemTypeFromCollection(
        string xml,
        List<ElementInfo> items,
        string itemTypeName,
        string itemElementName,
        XmlGenObservations obs)
    {
        // Take the union of scalars across all items. First non-empty observation
        // determines the kind. For collections and nested objects we also take
        // the union by name and merge their bodies recursively.
        var scalarByName = new Dictionary<string, XmlGenProperty>(StringComparer.Ordinal);
        var collectionByName = new Dictionary<string, XmlGenCollection>(StringComparer.Ordinal);
        var nestedByXmlName = new Dictionary<string, XmlGenNestedObject>(StringComparer.Ordinal);

        foreach (ElementInfo item in items)
        {
            (List<XmlGenProperty> itemScalars,
             List<XmlGenCollection> itemCollections,
             List<XmlGenNestedObject> itemNested) =
                ClassifyElementContents(xml, item, parentTypeName: itemTypeName, obs);

            foreach (XmlGenProperty s in itemScalars)
            {
                if (!scalarByName.ContainsKey(s.Name))
                {
                    scalarByName[s.Name] = s;
                }
                // Subsequent items keep the first observation's kind even if
                // their occurrence is empty — sample-driven type inference.
            }

            foreach (XmlGenCollection c in itemCollections)
            {
                if (!collectionByName.ContainsKey(c.CSharpPropertyName))
                {
                    collectionByName[c.CSharpPropertyName] = c;
                }
            }

            foreach (XmlGenNestedObject n in itemNested)
            {
                if (!nestedByXmlName.ContainsKey(n.XmlElementName))
                {
                    nestedByXmlName[n.XmlElementName] = n;
                }
            }
        }

        return new ItemTypeInfo(
            typeName: itemTypeName,
            xmlElementName: itemElementName,
            scalars: new List<XmlGenProperty>(scalarByName.Values),
            collections: new List<XmlGenCollection>(collectionByName.Values),
            nestedObjects: new List<XmlGenNestedObject>(nestedByXmlName.Values));
    }

    private static int NonXmlnsAttributeCount(IReadOnlyList<XmlGenProperty> attributes)
    {
        var n = 0;
        for (var i = 0; i < attributes.Count; i++)
        {
            if (!IsXmlnsAttribute(attributes[i].Name))
            {
                n++;
            }
        }
        return n;
    }

    private static bool IsXmlnsAttribute(string name)
    {
        return string.Equals(name, "xmlns", StringComparison.Ordinal)
            || name.StartsWith("xmlns:", StringComparison.Ordinal);
    }

    private static string Pluralize(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }
        // Crude rule per V1: name + "s" unless it already ends in "s".
        if (name[name.Length - 1] == 's' || name[name.Length - 1] == 'S')
        {
            return ClassNameDeriver.Derive(name);
        }
        return ClassNameDeriver.Derive(name) + "s";
    }

    // ── Element parsing (fully populates structural info) ────────────────────

    private sealed class ElementInfo
    {
        public string Name { get; init; } = string.Empty;
        public List<XmlGenProperty> Attributes { get; init; } = new List<XmlGenProperty>();
        public List<XmlGenProperty> PureTextChildren { get; init; } = new List<XmlGenProperty>();
        public List<ChildRef> StructuralChildren { get; init; } = new List<ChildRef>();
    }

    private readonly struct ChildRef
    {
        public ChildRef(string name, int position)
        {
            Name = name;
            Position = position;
        }

        public string Name { get; }
        public int Position { get; }
    }

    /// <summary>
    /// Parses a single element starting at <paramref name="p"/> (which must
    /// point at a <c>&lt;</c>). Advances <paramref name="p"/> to just past
    /// the closing <c>&gt;</c>. Returns null on any malformed input.
    /// </summary>
    private static ElementInfo? TryParseElementAt(
        string xml,
        ref int p,
        XmlGenObservations obs)
    {
        if (p >= xml.Length || xml[p] != '<')
        {
            return null;
        }

        p++; // consume '<'
        string name = ReadName(xml, ref p);
        if (name.Length == 0)
        {
            return null;
        }

        var info = new ElementInfo { Name = name };

        // Attributes
        while (true)
        {
            SkipWhitespace(xml, ref p);
            if (p >= xml.Length)
            {
                return null;
            }
            char c = xml[p];
            if (c == '/' || c == '>')
            {
                break;
            }
            XmlGenProperty? attr = ReadAttribute(xml, ref p, obs);
            if (attr == null)
            {
                return null;
            }
            info.Attributes.Add(attr);
        }

        // Self-closing → no inner content
        if (xml[p] == '/')
        {
            p++;
            if (p >= xml.Length || xml[p] != '>')
            {
                return null;
            }
            p++;
            return info;
        }

        p++; // consume '>'

        var sawCloseTag = false;
        while (p < xml.Length)
        {
            // Bare text between tags — consume but don't expose.
            if (xml[p] != '<')
            {
                while (p < xml.Length && xml[p] != '<')
                {
                    p++;
                }
                continue;
            }

            if (StartsWith(xml, p, "</"))
            {
                sawCloseTag = true;
                break;
            }
            if (StartsWith(xml, p, "<!--"))
            {
                int end = xml.IndexOf("-->", p + 4, StringComparison.Ordinal);
                if (end < 0)
                {
                    return null;
                }
                p = end + 3;
                continue;
            }
            if (StartsWith(xml, p, "<![CDATA["))
            {
                int end = xml.IndexOf("]]>", p + 9, StringComparison.Ordinal);
                if (end < 0)
                {
                    return null;
                }
                p = end + 3;
                continue;
            }

            // Child element. Determine if it's a pure-text scalar (and if so add
            // to PureTextChildren) or structural (add ChildRef).
            int childStart = p;
            ChildClassification? cls = ClassifyChild(xml, ref p, obs);
            if (cls == null)
            {
                return null;
            }
            if (cls.PureTextScalar != null)
            {
                info.PureTextChildren.Add(cls.PureTextScalar);
            }
            else
            {
                info.StructuralChildren.Add(new ChildRef(cls.ElementName, childStart));
            }
        }

        if (!sawCloseTag)
        {
            return null;
        }

        // Consume close tag </name>
        p += 2; // "</"
        string closeName = ReadName(xml, ref p);
        if (!string.Equals(closeName, name, StringComparison.Ordinal))
        {
            return null;
        }
        SkipWhitespace(xml, ref p);
        if (p >= xml.Length || xml[p] != '>')
        {
            return null;
        }
        p++;

        return info;
    }

    private sealed class ChildClassification
    {
        public string ElementName = string.Empty;
        public XmlGenProperty? PureTextScalar { get; set; }
    }

    /// <summary>
    /// Reads one child element starting at <paramref name="p"/>. Records
    /// the element name on the result regardless of pure-text vs structural
    /// classification. Advances <paramref name="p"/> past the closing tag.
    /// </summary>
    private static ChildClassification? ClassifyChild(
        string xml,
        ref int p,
        XmlGenObservations obs)
    {
        p++; // consume '<'
        string name = ReadName(xml, ref p);
        if (name.Length == 0)
        {
            return null;
        }

        var hasNonXmlnsAttr = false;
        var hasXmlnsAttr = false;
        while (true)
        {
            SkipWhitespace(xml, ref p);
            if (p >= xml.Length)
            {
                return null;
            }
            char c = xml[p];
            if (c == '/' || c == '>')
            {
                break;
            }
            int attrNameStart = p;
            string attrName = ReadName(xml, ref p);
            if (attrName.Length == 0)
            {
                return null;
            }
            if (IsXmlnsAttribute(attrName))
            {
                hasXmlnsAttr = true;
                obs.SawNamespaceDecl = true;
            }
            else
            {
                hasNonXmlnsAttr = true;
            }
            SkipWhitespace(xml, ref p);
            if (p >= xml.Length || xml[p] != '=')
            {
                return null;
            }
            p++;
            SkipWhitespace(xml, ref p);
            if (p >= xml.Length)
            {
                return null;
            }
            char quote = xml[p];
            if (quote != '"' && quote != '\'')
            {
                return null;
            }
            p++;
            int valEnd = xml.IndexOf(quote, p);
            if (valEnd < 0)
            {
                return null;
            }
            p = valEnd + 1;
        }

        var result = new ChildClassification { ElementName = name };

        // Self-closing without (non-xmlns) attributes → empty string scalar.
        if (xml[p] == '/')
        {
            p++;
            if (p >= xml.Length || xml[p] != '>')
            {
                return null;
            }
            p++;
            if (!hasNonXmlnsAttr)
            {
                result.PureTextScalar = new XmlGenProperty(name, XmlGenScalarKind.String, isAttribute: false);
            }
            return result;
        }

        p++; // consume '>'
        int contentStart = p;
        var sawNested = false;

        while (p < xml.Length)
        {
            if (StartsWith(xml, p, "</"))
            {
                break;
            }
            if (StartsWith(xml, p, "<!--"))
            {
                int end = xml.IndexOf("-->", p + 4, StringComparison.Ordinal);
                if (end < 0)
                {
                    return null;
                }
                p = end + 3;
                continue;
            }
            if (StartsWith(xml, p, "<![CDATA["))
            {
                sawNested = true;
                int end = xml.IndexOf("]]>", p + 9, StringComparison.Ordinal);
                if (end < 0)
                {
                    return null;
                }
                p = end + 3;
                continue;
            }
            if (xml[p] == '<')
            {
                sawNested = true;
                ChildClassification? probe = ClassifyChild(xml, ref p, obs);
                if (probe == null)
                {
                    return null;
                }
                continue;
            }
            p++;
        }

        if (p >= xml.Length)
        {
            return null;
        }

        int contentEnd = p;
        p += 2; // "</"
        string closeName = ReadName(xml, ref p);
        if (!string.Equals(closeName, name, StringComparison.Ordinal))
        {
            return null;
        }
        SkipWhitespace(xml, ref p);
        if (p >= xml.Length || xml[p] != '>')
        {
            return null;
        }
        p++;

        // Pure-text scalar requires: no nested elements, no CDATA, no non-xmlns
        // attributes. (xmlns attrs are dropped — STR0008 will surface them.)
        if (!hasNonXmlnsAttr && !sawNested)
        {
            string rawText = xml.Substring(contentStart, contentEnd - contentStart);
            string decoded = DecodeEntities(rawText, obs);
            result.PureTextScalar = new XmlGenProperty(name, InferKind(decoded), isAttribute: false);
        }
        return result;
    }

    private static XmlGenProperty? ReadAttribute(string xml, ref int p, XmlGenObservations obs)
    {
        string name = ReadName(xml, ref p);
        if (name.Length == 0)
        {
            return null;
        }
        SkipWhitespace(xml, ref p);
        if (p >= xml.Length || xml[p] != '=')
        {
            return null;
        }
        p++; // consume '='
        SkipWhitespace(xml, ref p);
        if (p >= xml.Length)
        {
            return null;
        }
        char quote = xml[p];
        if (quote != '"' && quote != '\'')
        {
            return null;
        }
        p++;
        int valStart = p;
        int valEnd = xml.IndexOf(quote, p);
        if (valEnd < 0)
        {
            return null;
        }
        string raw = xml.Substring(valStart, valEnd - valStart);
        p = valEnd + 1;
        string decoded = DecodeEntities(raw, obs);
        if (IsXmlnsAttribute(name))
        {
            obs.SawNamespaceDecl = true;
        }
        return new XmlGenProperty(name, InferKind(decoded), isAttribute: true);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void SkipProlog(string xml, ref int p, XmlGenObservations obs)
    {
        SkipWhitespace(xml, ref p);
        if (StartsWith(xml, p, "<?xml"))
        {
            int end = xml.IndexOf("?>", p + 5, StringComparison.Ordinal);
            if (end >= 0)
            {
                p = end + 2;
            }
        }
        // Skip leading whitespace, comments, and DTD.
        while (true)
        {
            SkipWhitespace(xml, ref p);
            if (StartsWith(xml, p, "<!--"))
            {
                int end = xml.IndexOf("-->", p + 4, StringComparison.Ordinal);
                if (end < 0)
                {
                    return;
                }
                p = end + 3;
                continue;
            }
            if (StartsWith(xml, p, "<!DOCTYPE"))
            {
                obs.SawDtd = true;
                SkipDoctype(xml, ref p);
                continue;
            }
            return;
        }
    }

    private static void SkipDoctype(string xml, ref int p)
    {
        // Pre: positioned at '<' of "<!DOCTYPE…>".
        int q = p + "<!DOCTYPE".Length;
        var insideSubset = false;
        while (q < xml.Length)
        {
            char c = xml[q];
            if (c == '[')
            {
                insideSubset = true;
            }
            else if (c == ']' && insideSubset)
            {
                int after = q + 1;
                while (after < xml.Length
                       && (xml[after] == ' ' || xml[after] == '\t'
                           || xml[after] == '\n' || xml[after] == '\r'))
                {
                    after++;
                }
                if (after < xml.Length && xml[after] == '>')
                {
                    p = after + 1;
                    return;
                }
            }
            else if (c == '>' && !insideSubset)
            {
                p = q + 1;
                return;
            }
            q++;
        }
        // Unterminated DOCTYPE — leave p where it was; the caller will fail
        // when it tries to parse the root element.
    }

    private static void SkipTrailing(string xml, ref int p, XmlGenObservations obs)
    {
        while (true)
        {
            SkipWhitespace(xml, ref p);
            if (StartsWith(xml, p, "<!--"))
            {
                int end = xml.IndexOf("-->", p + 4, StringComparison.Ordinal);
                if (end < 0)
                {
                    return;
                }
                p = end + 3;
                continue;
            }
            return;
        }
    }

    private static void SkipWhitespace(string xml, ref int p)
    {
        while (p < xml.Length)
        {
            char c = xml[p];
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

    private static bool StartsWith(string xml, int p, string s)
    {
        if (p + s.Length > xml.Length)
        {
            return false;
        }
        for (var i = 0; i < s.Length; i++)
        {
            if (xml[p + i] != s[i])
            {
                return false;
            }
        }
        return true;
    }

    private static string ReadName(string xml, ref int p)
    {
        if (p >= xml.Length || !IsNameStartChar(xml[p]))
        {
            return string.Empty;
        }
        int start = p;
        p++;
        while (p < xml.Length && IsNameChar(xml[p]))
        {
            p++;
        }
        return xml.Substring(start, p - start);
    }

    private static bool IsNameStartChar(char c)
    {
        return c == '_' || c == ':' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    }

    private static bool IsNameChar(char c)
    {
        return IsNameStartChar(c)
            || c == '-'
            || c == '.'
            || (c >= '0' && c <= '9');
    }

    private static string DecodeEntities(string raw, XmlGenObservations obs)
    {
        if (raw.IndexOf('&') < 0)
        {
            return raw;
        }
        var sb = new System.Text.StringBuilder(raw.Length);
        var i = 0;
        while (i < raw.Length)
        {
            char c = raw[i];
            if (c != '&')
            {
                sb.Append(c);
                i++;
                continue;
            }
            int semi = raw.IndexOf(';', i + 1);
            if (semi < 0)
            {
                sb.Append(c);
                i++;
                continue;
            }
            string body = raw.Substring(i + 1, semi - i - 1);
            switch (body)
            {
                case "amp": sb.Append('&'); i = semi + 1; continue;
                case "lt": sb.Append('<'); i = semi + 1; continue;
                case "gt": sb.Append('>'); i = semi + 1; continue;
                case "quot": sb.Append('"'); i = semi + 1; continue;
                case "apos": sb.Append('\''); i = semi + 1; continue;
            }
            if (body.Length > 1 && body[0] == '#')
            {
                int code;
                bool ok;
                if (body[1] == 'x' || body[1] == 'X')
                {
                    ok = int.TryParse(
                        body.Substring(2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out code);
                }
                else
                {
                    ok = int.TryParse(
                        body.Substring(1),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out code);
                }
                if (ok)
                {
                    sb.Append(char.ConvertFromUtf32(code));
                    i = semi + 1;
                    continue;
                }
            }

            // Unknown entity — preserve literal `&body;` and surface STR0007.
            obs.SawUnknownEntity = true;
            if (obs.FirstUnknownEntityName == null)
            {
                obs.FirstUnknownEntityName = body;
            }
            sb.Append('&').Append(body).Append(';');
            i = semi + 1;
        }
        return sb.ToString();
    }

    private static XmlGenScalarKind InferKind(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return XmlGenScalarKind.String;
        }
        if (string.Equals(trimmed, "true", StringComparison.Ordinal)
            || string.Equals(trimmed, "false", StringComparison.Ordinal))
        {
            return XmlGenScalarKind.Boolean;
        }
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return XmlGenScalarKind.Long;
        }
        if (trimmed.IndexOf('.') >= 0
            && decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return XmlGenScalarKind.Decimal;
        }
        return XmlGenScalarKind.String;
    }
}
