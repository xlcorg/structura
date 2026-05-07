using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Structura.Generator;

/// <summary>
/// Roslyn incremental source generator. For every <c>*.json</c>
/// AdditionalFile it emits one <c>{ClassName}.g.cs</c> containing a
/// strongly-typed document model in <c>Structura.Generated</c>. The
/// folder scanned for sample documents is configured via the
/// <c>StructuraJsonSamplesFolder</c> MSBuild property in the consuming
/// project (default: <c>Samples</c>) and glued to <c>AdditionalFiles</c>
/// by <c>build/Structura.Generator.props</c>.
/// </summary>
[Generator]
public sealed class StructuraJsonGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<AdditionalText> jsonFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".json",
                StringComparison.OrdinalIgnoreCase));

        IncrementalValuesProvider<(string filePath, string className, List<GenProperty> props, bool ok)> models =
            jsonFiles.Select(static (file, ct) =>
            {
                string text = file.GetText(ct)?.ToString() ?? string.Empty;
                string fileName = Path.GetFileName(file.Path);
                string className = ClassNameDeriver.Derive(fileName);
                List<GenProperty> props = GeneratorJsonParser.ParseRootProperties(text);
                // We treat empty-input files as parse failures only when the file
                // had non-whitespace content but produced no properties — a true
                // empty object {} is still valid (zero scalars).
                bool ok = !LooksLikeJunkInput(text) || props.Count > 0;
                return (file.Path, className, props, ok);
            });

        context.RegisterSourceOutput(models, static (spc, model) =>
        {
            string fileName = Path.GetFileName(model.filePath);
            Location fileLocation = Location.Create(
                model.filePath,
                Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(0, 0),
                new Microsoft.CodeAnalysis.Text.LinePositionSpan(
                    new Microsoft.CodeAnalysis.Text.LinePosition(0, 0),
                    new Microsoft.CodeAnalysis.Text.LinePosition(0, 0)));

            if (!model.ok)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    StructuraDiagnostics.InvalidJson,
                    fileLocation,
                    fileName,
                    "could not parse root object"));
                return;
            }

            string code = ModelEmitter.Emit(model.className, model.props);
            spc.AddSource(
                $"{model.className}.g.cs",
                SourceText.From(code, Encoding.UTF8));
        });
    }

    private static bool LooksLikeJunkInput(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                continue;
            }
            // First non-whitespace character must be '{' for a valid root object.
            return c != '{';
        }
        // All whitespace — not junk per se, just empty.
        return true;
    }
}
