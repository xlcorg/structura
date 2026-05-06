using System;
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

        IncrementalValuesProvider<(string className, XmlRootInfo? info)> models = xmlFiles.Select(static (file, ct) =>
        {
            string text = file.GetText(ct)?.ToString() ?? string.Empty;
            string fileName = Path.GetFileName(file.Path);
            string className = ClassNameDeriver.Derive(fileName);
            XmlRootInfo? info = GeneratorXmlParser.ParseRootInfo(text);
            return (className, info);
        });

        context.RegisterSourceOutput(models, static (spc, model) =>
        {
            if (model.info == null)
            {
                // Unparseable XML — skip emission to avoid breaking the build.
                return;
            }
            string code = XmlModelEmitter.Emit(model.className, model.info);
            spc.AddSource(
                $"{model.className}.g.cs",
                SourceText.From(code, Encoding.UTF8));
        });
    }
}
