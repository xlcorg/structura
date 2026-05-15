# Structura

A .NET library for **typed, minimal-patch editing of structured documents** (JSON and XML).

You point a source generator at a real sample document, mutate the resulting model through plain C# properties, and Structura rewrites **only the touched regions** of the original text. Whitespace, key/element ordering, comments, attribute style, and untouched content are preserved byte-for-byte.

This is **not** "deserialize → mutate → reserialize". A naive `System.Text.Json` round-trip would destroy formatting, comments, and ordering. Structura keeps origin spans on every node and applies a sorted list of non-overlapping text edits to the original string.

## Why

Use Structura when you need to:

- Patch one field in a config / contract / EDI document without churning the whole file in version control.
- Keep formatting intact for human reviewers (diffs that show only what changed).
- Round-trip messy real-world keys — `snake_case`, `kebab-case`, identifiers starting with a digit — without inventing your own DTO layer.

## How it works

1. A Roslyn **source generator** inspects a sample document marked as `AdditionalFiles` and emits a strongly-typed C# model that mirrors its shape into `Structura.Generated`.
2. The runtime parses the *original* document text into an instance of that model, recording the `(start, length)` span of every node.
3. You mutate properties; assignments mark the affected nodes as dirty.
4. `ToJson()` / `ToXml()` collects the dirty set, builds non-overlapping `TextEdit`s, and splices them into the original text.
5. `DiffReporter` renders the change-set as a unified or side-by-side diff.

## Project layout

Solution file: **`Structura.slnx`** (new XML format — tooling that only understands `.sln` will not see the projects).

| Project | Role |
| --- | --- |
| `src/Structura` | Console demo. Loads samples from `Samples/` and exercises the full pipeline. |
| `src/Structura.Generator` | Roslyn source generator. Emits one model class per sample file into `Structura.Generated`. |
| `src/Structura.Runtime` | Format-agnostic core: tree with origin spans, dirty tracking, patch engine, JSON and XML parsers/writers, `ParseJson<T>` / `ParseXml<T>` / `ToJson` / `ToXml`. |
| `src/Structura.Reporting` | `DiffReporter` — unified and side-by-side renderers over the runtime's change-set. |

## Build and run

```bash
dotnet build Structura.slnx
dotnet run --project src/Structura/Structura.csproj
```

## Wiring the generator into your project

In your csproj:

```xml
<PropertyGroup>
  <StructuraJsonSamplesFolder>Samples</StructuraJsonSamplesFolder>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="..\Structura.Generator\Structura.Generator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
  <ProjectReference Include="..\Structura.Runtime\Structura.Runtime.csproj" />
  <ProjectReference Include="..\Structura.Reporting\Structura.Reporting.csproj" />
</ItemGroup>

<Import Project="..\Structura.Generator\build\Structura.Generator.props" />

<ItemGroup>
  <None Include="Samples/*.json" CopyToOutputDirectory="PreserveNewest" />
  <None Include="Samples/*.xml" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Every `*.json` and `*.xml` file under `Samples/` is exposed to the generator as an `AdditionalFile`. The model type name is derived from the file name: `order.sample.json` → `OrderSampleJson`, `blrwbl.sample.xml` → `BlrwblSampleXml`. All generated types live in the `Structura.Generated` namespace.

## JSON example

Given `Samples/order.sample.json`:

```json
{
  "order_id": "ORD-2026-000123",
  "external-id": "erp-998877",
  "status": "Paid",
  "is_priority": true,
  "version": 7,
  "currency": "RUB",
  "customer": {
    "first_name": "Artem",
    "preferences": {
      "marketing-consent": true
    }
  }
}
```

…the generator emits `OrderSampleJson` with C# identifiers derived from the original keys (`external-id` → `ExternalId`, `2nd_line` → `SecondLine`, etc.). The original keys round-trip on save.

```csharp
using Structura.Generated;
using Structura.Reporting;
using Structura.Runtime;

string json = File.ReadAllText("Samples/order.sample.json");

var order = json.ParseJson<OrderSampleJson>();

order.Currency = "USD";
order.Version = 42;
order.IsPriority = false;
order.Customer.Preferences.MarketingConsent = false;

string patched = order.ToJson();
DiffReporter.Print(order);
```

`patched` contains the original text with only the four touched values rewritten. Whitespace, key order, and untouched lines are preserved.

## XML example

```csharp
using Structura.Generated;
using Structura.Reporting;
using Structura.Runtime;

string xml = File.ReadAllText("Samples/blrwbl.sample.xml");

var document = xml.ParseXml<BlrwblSampleXml>();

document.Document.DocumentID = "X-001";
document.Shipper.GLN = "9999988880001";
document.Total.TotalAmount = "700.00";

string patched = document.ToXml();
DiffReporter.Print(document);
```

The XML writer matches the surrounding style of the element it rewrites (indentation, attribute vs. element, single- vs. double-quoted attribute values).

## Reporting

`DiffReporter.Print(document)` reads from the document's `Changes` collection — the same change-set the patcher consumes — not by diffing strings after the fact.

```csharp
DiffReporter.Print(order);                                    // auto layout
DiffReporter.Print(order, new DiffReporterOptions
{
    Layout = DiffReporterLayout.SideBySide,
    ContextLines = 5,
    SyntaxHighlight = true,
    HorizontalRule = true,
});
```

`DiffReporterLayout.Auto` (default) picks side-by-side when the terminal is wide enough, and falls back to unified otherwise.

## Tests

```bash
dotnet test
# or a single test:
dotnet test --filter "FullyQualifiedName~Namespace.ClassName.TestMethod"
```

The strongest assertion for the patch engine is **byte-equality of untouched regions**: parse, mutate one property, save, and check that the bytes outside the patched span match the original exactly.

## Requirements

- .NET 10 SDK (`net10.0`, `Nullable=enable`, `ImplicitUsings=enable` — set centrally in `Directory.Build.props`).
- An IDE that understands `Structura.slnx` (or `dotnet` CLI 9+ for the new solution format).

## License

[MIT](LICENSE)
