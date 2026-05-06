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

        IncrementalValuesProvider<(string className, List<GenProperty> props)> models = jsonFiles.Select(static (file, ct) =>
        {
            string text      = file.GetText(ct)?.ToString() ?? string.Empty;
            string? fileName  = Path.GetFileName(file.Path);
            string className = ClassNameDeriver.Derive(fileName);
            List<GenProperty> props     = GeneratorJsonParser.ParseRootProperties(text);
            return (className, props);
        });

        context.RegisterSourceOutput(models, static (spc, model) =>
        {
            string code = ModelEmitter.Emit(model.className, model.props);
            spc.AddSource(
                $"{model.className}.g.cs",
                SourceText.From(code, Encoding.UTF8));
        });
    }
}
