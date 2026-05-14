using Structura.Runtime;

namespace Structura.Reporting.Internal;

/// <summary>
/// Builds the ordered list of <see cref="DiffLine"/> entries that
/// <see cref="UnifiedDiffReporter"/> renders. Groups changes whose old-line
/// ranges fall within <c>2 * ContextLines</c> of each other into a single
/// hunk, separated by <see cref="DiffLineKind.HunkSeparator"/>.
/// </summary>
internal static class DiffHunkBuilder
{
    public static IReadOnlyList<DiffLine> Build(IStructuraDocument document, DiffReporterOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<DocumentChange> changes = document.Changes;
        if (changes.Count == 0)
        {
            return Array.Empty<DiffLine>();
        }

        string[] oldLines = SplitLines(document.OriginalText);
        string[] newLines = SplitLines(document.CurrentText);

        List<ChangeRange> ranges = MapChangesToLineRanges(document.OriginalText, changes);

        if (options.ShowFullFile)
        {
            var fullOutput = new List<DiffLine>();
            HunkRange fullHunk = MakeFullFileHunk(ranges);
            int contextNeeded = ComputeFullFileContextLines(ranges, oldLines.Length);
            EmitContext fullCtx = new EmitContext(
                oldLines,
                newLines,
                document.OriginalText,
                contextNeeded,
                options.InlineHighlight);
            EmitHunk(fullOutput, fullHunk, fullCtx);
            return fullOutput;
        }

        List<HunkRange> hunks = GroupIntoHunks(ranges, options.ContextLines);
        EmitContext ctx = new EmitContext(
            oldLines,
            newLines,
            document.OriginalText,
            options.ContextLines,
            options.InlineHighlight);

        var output = new List<DiffLine>();
        for (var i = 0; i < hunks.Count; i++)
        {
            if (i > 0)
            {
                output.Add(new DiffLine(DiffLineKind.HunkSeparator, 0, 0, string.Empty, Array.Empty<ColumnRange>()));
            }
            EmitHunk(output, hunks[i], ctx);
        }

        return output;
    }

    private static string[] SplitLines(string text) =>
        text.Split('\n');

    private readonly record struct ChangeRange(
        int OldStartLine,   // 0-based
        int OldEndLine,     // 0-based inclusive
        int NewStartLine,
        int NewEndLine,
        DocumentChange Change);

    private readonly record struct HunkRange(
        int OldStartLine,
        int OldEndLine,
        int NewStartLine,
        int NewEndLine,
        List<ChangeRange> Changes);

    // Per-Build invariants threaded through the EmitHunk pipeline.
    private readonly record struct EmitContext(
        string[] OldLines,
        string[] NewLines,
        string OriginalText,
        int ContextLines,
        bool InlineHighlight);

    private static List<ChangeRange> MapChangesToLineRanges(string originalText, IReadOnlyList<DocumentChange> changes)
    {
        var result = new List<ChangeRange>(changes.Count);
        int cumulativeLineDelta = 0;
        foreach (DocumentChange c in changes)
        {
            int oldStart = LineOf(originalText, c.Span.Start);
            int oldEnd = c.Span.Length == 0
                ? oldStart
                : LineOf(originalText, c.Span.End - 1);
            int oldLineCount = oldEnd - oldStart + 1;
            int newLineCount = CountLineBreaks(c.NewText) + 1;

            int newStart = oldStart + cumulativeLineDelta;
            int newEnd = newStart + newLineCount - 1;
            result.Add(new ChangeRange(oldStart, oldEnd, newStart, newEnd, c));

            cumulativeLineDelta += newLineCount - oldLineCount;
        }
        return result;
    }

    private static int LineOf(string text, int offset)
    {
        var line = 0;
        for (var i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }
        return line;
    }

    private static int CountLineBreaks(string text)
    {
        var n = 0;
        foreach (char ch in text)
        {
            if (ch == '\n')
            {
                n++;
            }
        }
        return n;
    }

    private static List<HunkRange> GroupIntoHunks(List<ChangeRange> ranges, int contextLines)
    {
        var hunks = new List<HunkRange>();
        if (ranges.Count == 0)
        {
            return hunks;
        }

        int gap = 2 * contextLines;
        var current = new List<ChangeRange> { ranges[0] };
        for (var i = 1; i < ranges.Count; i++)
        {
            ChangeRange prev = current[^1];
            ChangeRange next = ranges[i];
            if (next.OldStartLine - prev.OldEndLine <= gap)
            {
                current.Add(next);
            }
            else
            {
                HunkRange completedHunk = MakeHunkRange(current);
                hunks.Add(completedHunk);
                current = new List<ChangeRange> { next };
            }
        }
        hunks.Add(MakeHunkRange(current));
        return hunks;
    }

    private static HunkRange MakeHunkRange(List<ChangeRange> group)
    {
        int oldStart = int.MaxValue, oldEnd = int.MinValue;
        int newStart = int.MaxValue, newEnd = int.MinValue;
        foreach (ChangeRange r in group)
        {
            if (r.OldStartLine < oldStart)
            {
                oldStart = r.OldStartLine;
            }
            if (r.OldEndLine > oldEnd)
            {
                oldEnd = r.OldEndLine;
            }
            if (r.NewStartLine < newStart)
            {
                newStart = r.NewStartLine;
            }
            if (r.NewEndLine > newEnd)
            {
                newEnd = r.NewEndLine;
            }
        }
        return new HunkRange(oldStart, oldEnd, newStart, newEnd, group);
    }

    private static void EmitHunk(List<DiffLine> output, HunkRange hunk, EmitContext ctx)
    {
        EmitPreContext(output, hunk, ctx);
        EmitChangeBody(output, hunk, ctx);
        EmitPostContext(output, hunk, ctx);
    }

    private static void EmitPreContext(List<DiffLine> output, HunkRange hunk, EmitContext ctx)
    {
        int preStart = Math.Max(0, hunk.OldStartLine - ctx.ContextLines);
        int preDelta = hunk.NewStartLine - hunk.OldStartLine;
        for (int i = preStart; i < hunk.OldStartLine; i++)
        {
            string strippedLine = StripCarriageReturn(ctx.OldLines[i]);
            int oldLineNumber = i + 1;
            int newLineNumber = i + 1 + preDelta;
            output.Add(new DiffLine(
                DiffLineKind.Context,
                oldLineNumber,
                newLineNumber,
                strippedLine,
                Array.Empty<ColumnRange>()));
        }
    }

    private static void EmitChangeBody(List<DiffLine> output, HunkRange hunk, EmitContext ctx)
    {
        // Walk changes in old-line order. Between consecutive changes (still
        // inside the hunk range), emit untouched old lines once as Context.
        // Inside a change range, emit Removed for old lines, Added for new lines.
        int oldCursor = hunk.OldStartLine;
        int newCursor = hunk.NewStartLine;
        List<ChangeRange> sortedChanges = new List<ChangeRange>(hunk.Changes);
        sortedChanges.Sort((a, b) => a.OldStartLine.CompareTo(b.OldStartLine));

        foreach (ChangeRange c in sortedChanges)
        {
            // Untouched lines between previous change and this one — emit as Context.
            // In an unchanged region the old and new line indices advance together.
            while (oldCursor < c.OldStartLine)
            {
                string contextContent = StripCarriageReturn(ctx.OldLines[oldCursor]);
                int oldLineNumber = oldCursor + 1;
                int newLineNumber = newCursor + 1;
                output.Add(new DiffLine(
                    DiffLineKind.Context,
                    oldLineNumber,
                    newLineNumber,
                    contextContent,
                    Array.Empty<ColumnRange>()));
                oldCursor++;
                newCursor++;
            }

            // Removed lines for this change (gutter = OLD line number).
            // Start from oldCursor (not c.OldStartLine) to skip lines already
            // emitted by a previous change on the same old-line range.
            int removedStart = Math.Max(c.OldStartLine, oldCursor);
            for (int i = removedStart; i <= c.OldEndLine; i++)
            {
                string content = StripCarriageReturn(ctx.OldLines[i]);
                IReadOnlyList<ColumnRange> highlights = ctx.InlineHighlight
                    ? CollectRemovedHighlightsForLine(i, hunk.Changes, ctx.OriginalText, content.Length)
                    : Array.Empty<ColumnRange>();
                int oldLineNumber = i + 1;
                output.Add(new DiffLine(DiffLineKind.Removed, oldLineNumber, 0, content, highlights));
            }
            if (c.OldEndLine + 1 > oldCursor)
            {
                oldCursor = c.OldEndLine + 1;
            }

            // Added lines for this change (gutter = NEW line number).
            // Start from newCursor (not c.NewStartLine) to skip lines already
            // emitted by a previous change on the same new-line range.
            int addedStart = Math.Max(c.NewStartLine, newCursor);
            for (int i = addedStart; i <= c.NewEndLine; i++)
            {
                string content = StripCarriageReturn(ctx.NewLines[i]);
                IReadOnlyList<ColumnRange> highlights = ctx.InlineHighlight
                    ? CollectAddedHighlightsForLine(i, hunk.Changes, content.Length, ctx.OriginalText)
                    : Array.Empty<ColumnRange>();
                int newLineNumber = i + 1;
                output.Add(new DiffLine(DiffLineKind.Added, 0, newLineNumber, content, highlights));
            }
            if (c.NewEndLine + 1 > newCursor)
            {
                newCursor = c.NewEndLine + 1;
            }
        }

        // Any tail of unchanged lines between the last change and hunk.OldEndLine
        // (rare — only if hunk grouping over-extended). Emit as Context.
        while (oldCursor <= hunk.OldEndLine)
        {
            string contextContent = StripCarriageReturn(ctx.OldLines[oldCursor]);
            int oldLineNumber = oldCursor + 1;
            int newLineNumber = newCursor + 1;
            output.Add(new DiffLine(
                DiffLineKind.Context,
                oldLineNumber,
                newLineNumber,
                contextContent,
                Array.Empty<ColumnRange>()));
            oldCursor++;
            newCursor++;
        }
    }

    private static void EmitPostContext(List<DiffLine> output, HunkRange hunk, EmitContext ctx)
    {
        int postEnd = Math.Min(ctx.OldLines.Length - 1, hunk.OldEndLine + ctx.ContextLines);
        int postDelta = hunk.NewEndLine - hunk.OldEndLine;
        for (int i = hunk.OldEndLine + 1; i <= postEnd; i++)
        {
            string strippedLine = StripCarriageReturn(ctx.OldLines[i]);
            int oldLineNumber = i + 1;
            int newLineNumber = i + 1 + postDelta;
            output.Add(new DiffLine(
                DiffLineKind.Context,
                oldLineNumber,
                newLineNumber,
                strippedLine,
                Array.Empty<ColumnRange>()));
        }
    }

    private static string StripCarriageReturn(string line) =>
        line.EndsWith("\r", StringComparison.Ordinal) ? line[..^1] : line;

    private static IReadOnlyList<ColumnRange> CollectRemovedHighlightsForLine(
        int oldLineIndex,
        List<ChangeRange> changes,
        string originalText,
        int contentLength)
    {
        var ranges = new List<ColumnRange>();
        foreach (ChangeRange c in changes)
        {
            if (oldLineIndex < c.OldStartLine || oldLineIndex > c.OldEndLine)
            {
                continue;
            }

            int lineStart = LineStartOffset(originalText, oldLineIndex);
            int colStart = oldLineIndex == c.OldStartLine ? c.Change.Span.Start - lineStart : 0;
            int colEnd = oldLineIndex == c.OldEndLine
                ? c.Change.Span.End - lineStart
                : contentLength;
            int clampedStart = Math.Clamp(colStart, 0, contentLength);
            int clampedEnd = Math.Clamp(colEnd, 0, contentLength);
            if (clampedEnd > clampedStart)
            {
                ranges.Add(new ColumnRange(clampedStart, clampedEnd - clampedStart));
            }
        }
        return ranges;
    }

    private static IReadOnlyList<ColumnRange> CollectAddedHighlightsForLine(
        int newLineIndex,
        List<ChangeRange> changes,
        int contentLength,
        string originalText)
    {
        var ranges = new List<ColumnRange>();
        foreach (ChangeRange c in changes)
        {
            if (newLineIndex < c.NewStartLine || newLineIndex > c.NewEndLine)
            {
                continue;
            }

            if (c.NewStartLine == c.NewEndLine && c.OldStartLine == c.OldEndLine)
            {
                int oldLineStart = LineStartOffset(originalText, c.OldStartLine);
                int colStart = c.Change.Span.Start - oldLineStart;
                int len = c.Change.NewText.Length;
                int clampedStart = Math.Clamp(colStart, 0, contentLength);
                int clampedEnd = Math.Clamp(colStart + len, 0, contentLength);
                if (clampedEnd > clampedStart)
                {
                    ranges.Add(new ColumnRange(clampedStart, clampedEnd - clampedStart));
                }
            }
            else
            {
                ranges.Add(new ColumnRange(0, contentLength));
            }
        }
        return ranges;
    }

    private static int LineStartOffset(string text, int lineIndex)
    {
        if (lineIndex <= 0)
        {
            return 0;
        }
        var seen = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                seen++;
                if (seen == lineIndex)
                {
                    return i + 1;
                }
            }
        }
        return text.Length;
    }

    private static HunkRange MakeFullFileHunk(List<ChangeRange> ranges)
    {
        int oldStart = ranges[0].OldStartLine;
        int oldEnd = ranges[^1].OldEndLine;
        int newStart = ranges[0].NewStartLine;
        int newEnd = ranges[^1].NewEndLine;
        return new HunkRange(oldStart, oldEnd, newStart, newEnd, ranges);
    }

    private static int ComputeFullFileContextLines(List<ChangeRange> ranges, int oldLineCount)
    {
        int beforeContext = ranges[0].OldStartLine;
        int afterContext = (oldLineCount - 1) - ranges[^1].OldEndLine;
        return Math.Max(beforeContext, afterContext);
    }
}
