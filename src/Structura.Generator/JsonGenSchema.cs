using System.Collections.Generic;

namespace Structura.Generator;

internal enum JsonGenScalarKind
{
    String,
    NullableString,
    Long,
    Decimal,
    Boolean,
}

internal sealed class JsonGenProperty
{
    public JsonGenProperty(string name, JsonGenScalarKind kind)
    {
        Name = name;
        Kind = kind;
    }

    public string Name { get; }
    public JsonGenScalarKind Kind { get; }
}

internal sealed class JsonGenObject
{
    public JsonGenObject(
        string typeName,
        List<JsonGenProperty> scalars,
        List<JsonGenNestedObject> nestedObjects,
        List<JsonGenCollection> collections,
        HashSet<string> requiredKeys)
    {
        TypeName = typeName;
        Scalars = scalars;
        NestedObjects = nestedObjects;
        Collections = collections;
        RequiredKeys = requiredKeys;
    }

    public string TypeName { get; }
    public List<JsonGenProperty> Scalars { get; }
    public List<JsonGenNestedObject> NestedObjects { get; }
    public List<JsonGenCollection> Collections { get; }

    /// <summary>
    /// Keys present in every observation contributing to this shape. For root
    /// objects (single observation) every observed key is required. For union
    /// shapes built from an array of objects, only keys seen in all items.
    /// </summary>
    public HashSet<string> RequiredKeys { get; }
}

internal sealed class JsonGenNestedObject
{
    public JsonGenNestedObject(string name, JsonGenObject obj)
    {
        Name = name;
        Object = obj;
    }

    public string Name { get; }
    public JsonGenObject Object { get; }
}

internal enum JsonGenItemKind
{
    /// <summary>Array of objects with a unified shape.</summary>
    Object,
    /// <summary>Array of homogeneous primitives.</summary>
    Primitive,
    /// <summary>Array with no observed items — type cannot be inferred.</summary>
    Empty,
    /// <summary>Array contains mixed shapes or incompatible primitive kinds.</summary>
    Heterogeneous,
}

internal sealed class JsonGenCollection
{
    public JsonGenCollection(
        string name,
        JsonGenItemKind itemKind,
        JsonGenObject? objectItem,
        JsonGenScalarKind? primitiveItemKind)
    {
        Name = name;
        ItemKind = itemKind;
        ObjectItem = objectItem;
        PrimitiveItemKind = primitiveItemKind;
    }

    public string Name { get; }
    public JsonGenItemKind ItemKind { get; }
    public JsonGenObject? ObjectItem { get; }
    public JsonGenScalarKind? PrimitiveItemKind { get; }
}

internal sealed class JsonRootInfo
{
    public JsonRootInfo(JsonGenObject root)
    {
        Root = root;
    }

    public JsonGenObject Root { get; }
}
