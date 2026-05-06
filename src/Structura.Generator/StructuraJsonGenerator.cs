using System;
using System.IO;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Structura.Generator;

/// <summary>
/// Roslyn incremental source generator. For every <c>*.sample.json</c>
/// AdditionalFile it emits one <c>{ClassName}.g.cs</c> containing a
/// strongly-typed document model in <c>Structura.Generated</c>.
/// </summary>
[Generator]
public sealed class StructuraJsonGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var jsonFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".sample.json",
                StringComparison.OrdinalIgnoreCase));

        var models = jsonFiles.Select(static (file, ct) =>
        {
            var text      = file.GetText(ct)?.ToString() ?? string.Empty;
            var fileName  = Path.GetFileName(file.Path);
            var className = ClassNameDeriver.Derive(fileName);
            var props     = GeneratorJsonParser.ParseRootProperties(text);
            return (className, props);
        });

        context.RegisterSourceOutput(models, static (spc, model) =>
        {
            var code = ModelEmitter.Emit(model.className, model.props);
            spc.AddSource(
                $"{model.className}.g.cs",
                SourceText.From(code, Encoding.UTF8));
        });
    }
}
