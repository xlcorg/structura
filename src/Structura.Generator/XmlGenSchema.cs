using System.Collections.Generic;

namespace Structura.Generator;

// ── Property kind discriminated union ────────────────────────────────────────

internal enum XmlGenScalarKind
{
    String,
    Long,
    Decimal,
    Boolean,
}

// ── Scalar property ──────────────────────────────────────────────────────────

internal sealed class XmlGenProperty
{
    public XmlGenProperty(string name, XmlGenScalarKind kind, bool isAttribute)
    {
        Name = name;
        Kind = kind;
        IsAttribute = isAttribute;
    }

    public string Name { get; }
    public XmlGenScalarKind Kind { get; }
    public bool IsAttribute { get; }
}

// ── Collections ──────────────────────────────────────────────────────────────

internal enum XmlGenCollectionStyle
{
    /// <summary>Wrapper element preserved as nested type (the user's BLRWBL pattern).</summary>
    Wrapper,

    /// <summary>Wrapper element collapsed because its name is the plural of the item name (Books/Book).</summary>
    Flat,

    /// <summary>Repeated same-name siblings inline, no wrapper.</summary>
    SiblingGroup,
}

internal sealed class XmlGenCollection
{
    public XmlGenCollection(
        XmlGenCollectionStyle style,
        string csharpPropertyName,
        string itemElementName,
        string itemTypeName,
        bool itemIsPureTextLeaf,
        string? wrapperElementName,
        ItemTypeInfo? item)
    {
        Style = style;
        CSharpPropertyName = csharpPropertyName;
        ItemElementName = itemElementName;
        ItemTypeName = itemTypeName;
        ItemIsPureTextLeaf = itemIsPureTextLeaf;
        WrapperElementName = wrapperElementName;
        Item = item;
    }

    public XmlGenCollectionStyle Style { get; }

    /// <summary>C# property name on the parent type (e.g. <c>"LineItems"</c>).</summary>
    public string CSharpPropertyName { get; }

    /// <summary>The XML element name of each item (e.g. <c>"LineItem"</c>).</summary>
    public string ItemElementName { get; }

    /// <summary>The C# type name of each item, or <c>"string"</c> for pure-text-leaf collections.</summary>
    public string ItemTypeName { get; }

    /// <summary>True when items are pure text leaves; <see cref="Item"/> is null in that case.</summary>
    public bool ItemIsPureTextLeaf { get; }

    /// <summary>For <see cref="XmlGenCollectionStyle.Wrapper"/> only: the wrapper element name.</summary>
    public string? WrapperElementName { get; }

    /// <summary>Recursive item type description; null when <see cref="ItemIsPureTextLeaf"/>.</summary>
    public ItemTypeInfo? Item { get; }
}

internal sealed class ItemTypeInfo
{
    public ItemTypeInfo(
        string typeName,
        string xmlElementName,
        List<XmlGenProperty> scalars,
        List<XmlGenCollection> collections,
        List<XmlGenNestedObject> nestedObjects)
    {
        TypeName = typeName;
        XmlElementName = xmlElementName;
        Scalars = scalars;
        Collections = collections;
        NestedObjects = nestedObjects;
    }

    public string TypeName { get; }
    public string XmlElementName { get; }
    public List<XmlGenProperty> Scalars { get; }
    public List<XmlGenCollection> Collections { get; }
    public List<XmlGenNestedObject> NestedObjects { get; }
}

// ── Nested object ────────────────────────────────────────────────────────────

/// <summary>
/// A pure-structural single-occurrence child element classified as a nested
/// object (Step 10): the runtime exposes it as a typed property rather than
/// flattening it. The body re-uses <see cref="ItemTypeInfo"/> because a nested
/// object's content surface is identical to a collection item's: scalars,
/// collections, and (recursively) further nested objects.
/// </summary>
internal sealed class XmlGenNestedObject
{
    public XmlGenNestedObject(string xmlElementName, ItemTypeInfo body)
    {
        XmlElementName = xmlElementName;
        Body = body;
    }

    public string XmlElementName { get; }
    public ItemTypeInfo Body { get; }
}

// ── Generator-side observations (for diagnostics) ─────────────────────────────

internal sealed class XmlGenObservations
{
    public bool SawDtd { get; set; }
    public bool SawUnknownEntity { get; set; }
    public bool SawNamespaceDecl { get; set; }

    /// <summary>List of (parentTypeName, elementName) pairs for STR0009.</summary>
    public List<(string ParentType, string ElementName)> SkippedStructural { get; }
        = new List<(string, string)>();

    public string? FirstUnknownEntityName { get; set; }
}

// ── Result ───────────────────────────────────────────────────────────────────

internal sealed class XmlRootInfo
{
    public XmlRootInfo(
        string rootName,
        IReadOnlyList<string> wrapperChain,
        List<XmlGenProperty> scalars,
        List<XmlGenCollection> collections,
        List<XmlGenNestedObject> nestedObjects,
        XmlGenObservations observations)
    {
        RootName = rootName;
        WrapperChain = wrapperChain;
        Scalars = scalars;
        Collections = collections;
        NestedObjects = nestedObjects;
        Observations = observations;
    }

    public string RootName { get; }
    public IReadOnlyList<string> WrapperChain { get; }
    public List<XmlGenProperty> Scalars { get; }
    public List<XmlGenCollection> Collections { get; }
    public List<XmlGenNestedObject> NestedObjects { get; }
    public XmlGenObservations Observations { get; }
}
