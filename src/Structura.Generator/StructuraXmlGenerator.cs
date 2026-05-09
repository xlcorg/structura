using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Structura.Generator;

/// <summary>
/// Roslyn incremental source generator for XML samples. For every <c>*.xml</c>
/// AdditionalFile it emits one <c>{ClassName}.g.cs</c> containing a
/// strongly-typed document model in <c>Structura.Generated</c>. The folder
/// scanned is configured via the <c>StructuraJsonSamplesFolder</c> MSBuild
/// property (shared with the JSON generator) and glued to
/// <c>AdditionalFiles</c> by <c>build/Structura.Generator.props</c>.
/// </summary>
[Generator]
public sealed class StructuraXmlGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<AdditionalText> xmlFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".xml",
                StringComparison.OrdinalIgnoreCase));

        IncrementalValuesProvider<(string filePath, string className, XmlRootInfo? info)> models =
            xmlFiles.Select(static (file, ct) =>
            {
                string text = file.GetText(ct)?.ToString() ?? string.Empty;
                string fileName = Path.GetFileName(file.Path);
                string className = ClassNameDeriver.Derive(fileName);
                XmlRootInfo? info = GeneratorXmlParser.ParseRootInfo(text);
                return (file.Path, className, info);
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

            if (model.info == null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    StructuraDiagnostics.InvalidXml,
                    fileLocation,
                    fileName,
                    "could not parse root element"));
                return;
            }

            // Surface observation flags as warnings.
            XmlGenObservations obs = model.info.Observations;
            if (obs.SawDtd)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    StructuraDiagnostics.SkippedDtd,
                    fileLocation,
                    fileName));
            }
            if (obs.SawUnknownEntity)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    StructuraDiagnostics.UnknownEntity,
                    fileLocation,
                    fileName,
                    obs.FirstUnknownEntityName ?? "unknown"));
            }
            if (obs.SawNamespaceDecl)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    StructuraDiagnostics.NamespaceDeclaration,
                    fileLocation,
                    fileName));
            }

            // STR0009 once per (parentType, elementName).
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string parentType, string elementName) in obs.SkippedStructural)
            {
                string key = parentType + "|" + elementName;
                if (seen.Add(key))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        StructuraDiagnostics.UnsupportedStructuralElement,
                        fileLocation,
                        fileName,
                        elementName,
                        parentType));
                }
            }

            string code = XmlModelEmitter.Emit(model.className, model.info);
            spc.AddSource(
                $"{model.className}.g.cs",
                SourceText.From(code, Encoding.UTF8));
        });
    }
}
