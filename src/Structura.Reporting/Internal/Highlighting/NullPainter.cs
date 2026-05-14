namespace Structura.Reporting.Internal.Highlighting;

/// <summary>
/// No-op painter used when syntax highlighting is disabled or the format is
/// unknown. Returns an empty token list; the renderer's coalesced walker
/// then degenerates to plain content emission.
/// </summary>
internal sealed class NullPainter : IDiffSyntaxPainter
{
    public static readonly NullPainter Instance = new NullPainter();

    private NullPainter() { }

    public IReadOnlyList<TokenRange> TokenizeLine(string content) =>
        Array.Empty<TokenRange>();
}
