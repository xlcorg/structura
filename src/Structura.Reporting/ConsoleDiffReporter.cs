using Structura.Runtime;

namespace Structura.Reporting;

/// <summary>
/// Per-change git-style diff printer. For every <see cref="DocumentChange"/>
/// emits a hunk like
/// <code>
/// @@ /path (line N) @@
/// - originalLineContainingTheSpan
/// + sameLineWithOldTextReplacedByNewText
/// </code>
/// When writing to <see cref="Console.Out"/> on an interactive terminal the
/// minus and plus lines are rendered in red and green respectively. The
/// <see cref="TextWriter"/> overload always emits plain text so tests can
/// assert exact buffer contents.
/// </summary>
public static class ConsoleDiffReporter
{
    public static void Print(IStructuraDocument document)
    {
        RenderTo(document, Console.Out, useColor: !Console.IsOutputRedirected);
    }

    public static void Print(IStructuraDocument document, TextWriter writer)
    {
        RenderTo(document, writer, useColor: false);
    }

    private static void RenderTo(IStructuraDocument document, TextWriter writer, bool useColor)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);

        IReadOnlyList<DocumentChange> changes = document.Changes;
        if (changes.Count == 0)
        {
            writer.WriteLine("(no changes)");
            return;
        }

        for (var i = 0; i < changes.Count; i++)
        {
            DocumentChange change = changes[i];
            LineContext ctx = LineContext.Of(document.OriginalText, change.Span);
            string newLine = ctx.Line[..ctx.ColumnStart]
                             + change.NewText
                             + ctx.Line[(ctx.ColumnStart + change.OldText.Length)..];

            writer.WriteLine($"@@ {change.Path} (line {ctx.LineNumber}) @@");
            WriteColoredLine(writer, useColor, ConsoleColor.Red, $"- {ctx.Line}");
            WriteColoredLine(writer, useColor, ConsoleColor.Green, $"+ {newLine}");
            if (i < changes.Count - 1)
            {
                writer.WriteLine();
            }
        }
    }

    private static void WriteColoredLine(TextWriter writer, bool useColor, ConsoleColor color, string line)
    {
        if (!useColor || writer != Console.Out)
        {
            writer.WriteLine(line);
            return;
        }

        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        try
        {
            writer.WriteLine(line);
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }

    private readonly record struct LineContext(string Line, int LineNumber, int ColumnStart)
    {
        public static LineContext Of(string text, TextSpan span)
        {
            int lineStart = text.LastIndexOf('\n', Math.Max(0, span.Start - 1)) + 1;
            int lineEnd = text.IndexOf('\n', span.Start);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            string line = text[lineStart..lineEnd].TrimEnd('\r');
            int lineNumber = 1 + CountChar(text.AsSpan(0, lineStart), '\n');
            return new LineContext(line, lineNumber, span.Start - lineStart);
        }

        private static int CountChar(ReadOnlySpan<char> s, char c)
        {
            var n = 0;
            foreach (char ch in s)
            {
                if (ch == c)
                {
                    n++;
                }
            }
            return n;
        }
    }
}
