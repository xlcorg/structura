# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What Structura is

Structura is a .NET library for **typed, minimal-patch editing of structured documents** (JSON and XML).

The user-facing flow is:

1. A C# Source Generator inspects a concrete sample document (e.g. `order.sample.json`) and emits a strongly-typed C# model that mirrors its shape.
2. The runtime parses the *original* document text into an instance of that model.
3. The user mutates the model through plain C# properties.
4. The runtime saves the changes back as text — but **only the touched regions are rewritten**. Whitespace, key/element ordering, comments, and untouched parts are preserved byte-for-byte.
5. `SimpleReporter` and `ConsoleDiffReporter` show what changed.

The defining constraint is rule 4–5: this is **not** "deserialize → mutate → reserialize". A naive `System.Text.Json` round-trip would destroy formatting, comments, and ordering, which violates the core promise. Any design that loses fidelity outside of mutated spans is wrong.

## What this means for the design

- **Origin tracking**: parsing must record, for every node in the model, the exact `(start, length)` span in the source text that produced it (and, for objects, the spans of each key/value pair separator, so insertions/deletions can splice cleanly).
- **Dirty tracking**: the generated model must mark which properties/collections were assigned after parsing. Untouched nodes contribute no edits.
- **Patch application**: saving = collecting the dirty set, producing a sorted list of non-overlapping `TextEdit { Span, Replacement }`, and splicing them into the original string. The serializer is per-leaf-value, not whole-document.
- **JSON and XML share the same shape**: both formats need a parser that yields a tree with origin spans, a model layer with dirty tracking, and a writer that emits replacement text for a single value while matching the surrounding style (quote style, indentation, attribute vs. element, etc.). Reuse the abstractions; don't duplicate them.
- **Sample-driven generation**: the schema comes from a real document, not a JSON Schema or XSD. The generator must cope with messy real-world keys — the bundled `order.sample.json` deliberately mixes `snake_case`, `kebab-case` (`external-id`, `marketing-consent`), and identifiers starting with a digit (`2nd_line`); valid C# identifiers must be derived from these without collisions, and the original key must round-trip on save.

## Public surface (what `Program.cs` already commits to)

```csharp
using Structura.Generated;     // emitted by the source generator
using Structura.Runtime;       // ParseJson<T>, ToJson extensions
using Structura.Reporting;     // SimpleReporter, ConsoleDiffReporter

var order = json.ParseJson<OrderSampleJson>();
order.Currency = "USD";
var modifiedJson = order.ToJson();
SimpleReporter.Print(order);
ConsoleDiffReporter.Print(order);
```

`ToJson()` returns the **patched original**, not a freshly serialized one. Reporters operate on the same change-set the patcher consumes, so they must read from the model's dirty-tracking state, not by diffing strings post-hoc (string diff is a fallback at best, not the source of truth).

The generator is expected to derive the model type name from the sample file name (`order.sample.json` → `OrderSampleJson`) and place it in `Structura.Generated`.

## Project layout

Solution file: **`Structura.slnx`** (new XML format — tooling that only understands `.sln` won't see the projects).

| Project | Role |
| --- | --- |
| `src/Structura` | Console host / demo. Loads `Samples/order.sample.json` and exercises the full pipeline. |
| `src/Structura.Generator` | Roslyn source generator. Reads sample documents marked as `AdditionalFiles` and emits the generated model + origin/dirty-tracking glue into `Structura.Generated`. Needs `<IsRoslynComponent>true</IsRoslynComponent>`; consumers reference it with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`. |
| `src/Structura.Runtime` | Format-agnostic core: document tree with origin spans, dirty tracking, patch engine, plus JSON and XML parsers/writers. Hosts `ParseJson<T>` / `ToJson` (and the XML equivalents). |
| `src/Structura.Reporting` | `SimpleReporter` (flat list of changes) and `ConsoleDiffReporter` (colored before/after slices). Pure consumers of the runtime's change-set; no parsing logic. |

## Build and run

```bash
dotnet build Structura.slnx
dotnet run --project src/Structura/Structura.csproj
```

`Program.cs` reads `Samples/order.sample.json` via a relative path — either run from `src/Structura/` or mark the sample as `<Content CopyToOutputDirectory="PreserveNewest">` in the csproj.

## Tests

Place test projects under `tests/` (folder is already declared in the solution). `Directory.Build.targets` exposes internals to an assembly named **`Structura.IntegrationTests`** — name the integration test project that exact string to inherit `InternalsVisibleTo` automatically.

Test packages (xunit 2.9, FluentAssertions 7, Moq, coverlet, `Microsoft.AspNetCore.Mvc.Testing`) are pre-pinned in `Directory.Packages.props`. Reference them without a `Version` attribute — central package management is on.

Run a single test:

```bash
dotnet test --filter "FullyQualifiedName~Namespace.ClassName.TestMethod"
```

When writing tests for the patch engine, the strongest assertion is **byte-equality** of untouched regions: parse, mutate one property, save, and check that the bytes outside the patched span match the original exactly.

## Repo conventions

- **Target framework** `net10.0`, `Nullable=enable`, `ImplicitUsings=enable` — all set centrally in `Directory.Build.props`. Do not redeclare per-project.
- **Central Package Management** (`Directory.Packages.props`): add new deps as `<PackageVersion>` there, then `<PackageReference Include="..." />` in the csproj with no version.
- **`.editorconfig` is authoritative** and ReSharper-flavored. Notable rules: file-scoped namespaces (warning), braces always required (error), `var` when the type is evident, private constants / `static readonly` fields are `UpperCamelCase`, max line length 170, LF endings, 4-space indent.
