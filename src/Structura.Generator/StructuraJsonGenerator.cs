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

        IncrementalValuesProvider<(string filePath, string className, JsonRootInfo? info, bool ok)> models =
            jsonFiles.Select(static (file, ct) =>
            {
                string text = file.GetText(ct)?.ToString() ?? string.Empty;
                string fileName = Path.GetFileName(file.Path);
                string className = ClassNameDeriver.Derive(fileName);
                JsonRootInfo? info = GeneratorJsonParser.ParseRootInfo(text);
                bool ok = info is not null || !LooksLikeJunkInput(text);
                return (file.Path, className, info, ok);
            });

        context.RegisterSourceOutput(models, static (spc, model) =>
        {
            string fileName = Path.GetFileName(model.filePath);
            var fileLocation = Location.Create(
                model.filePath,
                TextSpan.FromBounds(0, 0),
                new LinePositionSpan(
                    new LinePosition(0, 0),
                    new LinePosition(0, 0)));

            if (!model.ok || model.info is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    StructuraDiagnostics.InvalidJson,
                    fileLocation,
                    fileName,
                    "could not parse root object"));
                return;
            }

            ReportArrayDiagnostics(spc, fileLocation, fileName, model.info.Root);

            string code = JsonModelEmitter.Emit(model.className, fileName, model.info);
            spc.AddSource(
                $"{model.className}.g.cs",
                SourceText.From(code, Encoding.UTF8));
        });
    }

    /// <summary>
    /// Reports STR0010 (heterogeneous array) and STR0011 (indeterminate empty
    /// array) for every collection in the schema that the emitter is going to
    /// drop. Walks nested objects + object-array items recursively so the
    /// warning fires for arrays at any depth.
    /// </summary>
    private static void ReportArrayDiagnostics(
        SourceProductionContext spc,
        Location fileLocation,
        string fileName,
        JsonGenObject obj)
    {
        foreach (JsonGenCollection coll in obj.Collections)
        {
            switch (coll.ItemKind)
            {
                case JsonGenItemKind.Heterogeneous:
                    spc.ReportDiagnostic(Diagnostic.Create(
                        StructuraDiagnostics.HeterogeneousArray,
                        fileLocation,
                        fileName,
                        coll.Name));
                    break;
                case JsonGenItemKind.Empty:
                    spc.ReportDiagnostic(Diagnostic.Create(
                        StructuraDiagnostics.IndeterminateEmptyArray,
                        fileLocation,
                        fileName,
                        coll.Name));
                    break;
                case JsonGenItemKind.Object:
                    if (coll.ObjectItem is not null)
                    {
                        ReportArrayDiagnostics(spc, fileLocation, fileName, coll.ObjectItem);
                    }
                    break;
            }
        }

        foreach (JsonGenNestedObject n in obj.NestedObjects)
        {
            ReportArrayDiagnostics(spc, fileLocation, fileName, n.Object);
        }
    }

    private static bool LooksLikeJunkInput(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                continue;
            }
            return c != '{';
        }
        return true;
    }
}
