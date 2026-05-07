using Microsoft.CodeAnalysis;

namespace Structura.Generator;

/// <summary>
/// Roslyn <see cref="DiagnosticDescriptor"/> catalogue used by both
/// <c>StructuraJsonGenerator</c> and <c>StructuraXmlGenerator</c>. Each
/// descriptor carries a stable <c>STRxxxx</c> id plus a message format the
/// caller fills with <see cref="Diagnostic.Create(DiagnosticDescriptor, Location?, object?[])"/>.
/// </summary>
internal static class StructuraDiagnostics
{
    private const string Category = "Structura.Generator";

    public static readonly DiagnosticDescriptor InvalidJson = new DiagnosticDescriptor(
        id: "STR0001",
        title: "Invalid JSON",
        messageFormat: "Could not parse '{0}' as JSON: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidXml = new DiagnosticDescriptor(
        id: "STR0002",
        title: "Invalid XML",
        messageFormat: "Could not parse '{0}' as XML: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedRootShape = new DiagnosticDescriptor(
        id: "STR0003",
        title: "Unsupported root shape",
        messageFormat: "'{0}' has an unsupported root shape: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedRepeatedShape = new DiagnosticDescriptor(
        id: "STR0004",
        title: "Unsupported repeated-element shape",
        messageFormat: "Repeated-element shape in '{0}' is not supported: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateMemberName = new DiagnosticDescriptor(
        id: "STR0005",
        title: "Duplicate generated member name",
        messageFormat: "Two source names in '{0}' sanitise to the same C# identifier '{1}' even after collision suffixes",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SkippedDtd = new DiagnosticDescriptor(
        id: "STR0006",
        title: "Unsupported feature: DTD",
        messageFormat: "Skipped <!DOCTYPE> in '{0}', entity declarations are not honoured",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnknownEntity = new DiagnosticDescriptor(
        id: "STR0007",
        title: "Unsupported feature: custom entity reference",
        messageFormat: "Unknown entity reference '&{1};' in '{0}', literal text preserved",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NamespaceDeclaration = new DiagnosticDescriptor(
        id: "STR0008",
        title: "Unsupported feature: XML namespace",
        messageFormat: "Dropped 'xmlns' / 'xmlns:*' declarations from scalar discovery in '{0}', namespace-prefixed elements are exposed by their literal name",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedStructuralElement = new DiagnosticDescriptor(
        id: "STR0009",
        title: "Unsupported nested structural element",
        messageFormat: "Skipped '{1}' in '{2}' ('{0}'), nested generation is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HeterogeneousArray = new DiagnosticDescriptor(
        id: "STR0010",
        title: "Heterogeneous JSON array",
        messageFormat: "JSON array '{1}' in '{0}' contains items of mixed shapes or incompatible primitive kinds; the property is skipped",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IndeterminateEmptyArray = new DiagnosticDescriptor(
        id: "STR0011",
        title: "Indeterminate empty JSON array",
        messageFormat: "JSON array '{1}' in '{0}' is empty and has no sibling observations to infer the item type from; the property is skipped",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
