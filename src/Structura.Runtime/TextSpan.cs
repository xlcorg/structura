namespace Structura.Runtime;

/// <summary>
/// Half-open span [Start, Start+Length) over a source string.
/// </summary>
public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;

    public static TextSpan FromBounds(int start, int end)
    {
        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), end, "End must be greater than or equal to start.");
        }
        return new TextSpan(start, end - start);
    }

    public bool IntersectsWith(TextSpan other)
    {
        return Start < other.End && other.Start < End;
    }

    public override string ToString()
    {
        return $"[{Start}..{End})";
    }
}
