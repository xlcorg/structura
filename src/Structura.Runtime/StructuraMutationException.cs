namespace Structura.Runtime;

/// <summary>
/// Thrown by a generated model setter when the requested mutation cannot
/// be applied because the underlying source span doesn't exist. The
/// canonical V1 trigger is the heterogeneous-item field-union: a property
/// exists on the item type because some sibling carried the field, but
/// this particular item's source XML/JSON is missing the corresponding
/// element/attribute. V1 has no insertion-aware patch engine, so we
/// surface a loud failure rather than silently dropping the write.
/// </summary>
public sealed class StructuraMutationException : Exception
{
    public StructuraMutationException(string message)
        : base(message)
    {
    }

    public StructuraMutationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
