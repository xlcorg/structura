# Step 14 — XML scalars are always `string`

## Goal

Stop inferring C# scalar types from XML sample values. Every element /
attribute scalar in a generated XML model becomes `string`, regardless of
what the bundled sample happens to contain. The asymmetric inference that
makes `<year>1869</year>` → `long` and `<verified>true</verified>` → `bool`
is removed entirely.

The reason is structural: XML carries no syntactic type information. A
one-shot heuristic on the first observed value is fragile (`<TaxRate>20`
breaks the moment a real document writes `20.00`; `<verified>true` breaks
the moment one writes `Yes`). Always-string is the only honest mapping
for the document's surface.

It also fits the project's defining constraint (see CLAUDE.md): "not
deserialize → mutate → reserialize". Promoting `"1869"` to a `long` is a
small deserialize step that buys nothing in return — minimal-patch
mutation operates on the byte span, not on the C# value.

## Non-goals

- **JSON is not touched.** JSON has typed literals in its grammar
  (`123` ≠ `"123"`, `true` ≠ `"true"`). Inference there is principled
  and the JSON generator (`JsonModelEmitter`, `JsonGenSchema`,
  `JsonSourceParser`) keeps its full set of `string` / `long` / `double` /
  `decimal` / `bool` scalar kinds and its corresponding writer methods in
  `JsonValueWriter`.
- **No opt-in typed XML scalars.** No attribute, no partial-class
  extension, no `[XmlScalar(typeof(long))]` annotation. YAGNI for V1; if
  a client truly needs a typed property they can declare a computed
  property on the partial class and call `int.Parse` / `decimal.Parse`
  themselves.
- **No XML schema (XSD) support.** Same reason — out of V1 scope and
  this step doesn't change that.
- **No runtime API surface changes beyond the writer prune.** The
  `IStructuraXmlDocument<TSelf>` interface, `XmlSourceParser`,
  `XmlSourceElement`, `XmlSourceAttribute`, `XmlSourceText`,
  `TextEdit`, and the patch engine stay byte-identical.

## Affected surface (concrete)

The change touches three files in the generator, one file in the runtime,
the demo host, and a handful of tests.

### Generator (`src/Structura.Generator/`)

`XmlGenSchema.cs`:
- Delete `enum XmlGenScalarKind` entirely.
- `XmlGenProperty` constructor and `Kind` property go away. The class
  collapses to `(string Name, bool IsAttribute)` — a record-shaped pair.
  Keep it as a sealed class for API parity with `XmlGenCollection` /
  `XmlGenNestedObject`.

`GeneratorXmlParser.cs`:
- Delete the `InferKind` method (lines ~1070-1092 today).
- Delete the `System.Globalization` `using` if `InferKind` was its only
  caller (it is — confirm at edit time).
- Every `new XmlGenProperty(name, XmlGenScalarKind.X, isAttribute: ...)`
  call site drops the kind argument:
  - line ~724 (self-closing element → empty-string scalar) — drop `, XmlGenScalarKind.String`.
  - line ~798 (pure-text element scalar) — drop `, InferKind(decoded)`.
  - line ~840 (attribute scalar) — drop `, InferKind(decoded)`.
- The `decoded` local from `DecodeEntities(raw, obs)` is no longer used
  to feed `InferKind`. It IS still used to set
  `obs.SawUnknownEntity` / `obs.FirstUnknownEntityName` via the
  `DecodeEntities` call's side effects — so the call stays, the return
  value can be discarded with `_`.

`XmlModelEmitter.cs`:
- Delete `AttributeBindings`, `ElementBindings`, and `DefaultValueExpr`
  methods (lines ~1045-1106). Inline their string-kind branches at the
  three call sites:
  - `BuildRootScalar` (attribute branch): `csharpType = "string"`,
    `ctorExpr = $"{varName}.Value"`,
    `writerCall = "XmlValueWriter.WriteAttributeValue(value)"`.
  - `BuildRootScalar` (element branch): `csharpType = "string"`,
    `ctorExpr = $"((XmlSourceText){varName}El.Children[0]).Value"`,
    `writerCall = "XmlValueWriter.WriteElementText(value)"`.
  - `BuildItemScalar` (attribute and element branches): same shapes but
    using `textExpr` for the safe-on-empty pattern that already exists.
  - `BuildNestedScalar` (attribute and element branches): same.
- `ItemScalar.Kind` field — delete. Default value for absent scalars
  is `"string.Empty"` everywhere (previously `DefaultValueExpr(kind)`).
- The two helper records `RootScalar` / `ItemScalar` / `NestedScalar`
  lose any field that exists solely to carry kind metadata. After the
  cut they are pure binding records: pascal/camel names, lookup expr,
  ctor expr, writer call, path, and (for `ItemScalar`) the `IsAttribute`
  + `LookupVarName` for the IsPresent guard.
- Drop the `using System.Globalization;` line emitted at the top of the
  generated file (line ~28). It was there for `CultureInfo.InvariantCulture`
  inside `long.Parse` / `decimal.Parse` / `bool.Parse`, none of which
  remain.

### Runtime (`src/Structura.Runtime/Xml/`)

`XmlValueWriter.cs`:
- Delete `WriteInt64`, `WriteInt32`, `WriteDouble`, `WriteDecimal`,
  `WriteBoolean` (lines 84-109). They become dead code as soon as the
  emitter stops calling them.
- Drop `using System.Globalization;` (only the deleted methods needed it).
- Keep `WriteElementText` and `WriteAttributeValue` exactly as they are.

### Demo (`src/Structura/Program.cs`)

Five typed assignments / comparisons on the BLRWBL pipeline become
string-typed:

| Line | Today | After |
|------|-------|-------|
| 54   | `waybill.SealID = 99999;` | `waybill.SealID = "99999";` |
| 56   | `waybill.Shipper.GLN = 9999988880001L;` | `waybill.Shipper.GLN = "9999988880001";` |
| 57   | `waybill.Total.TotalAmount = 700.00m;` | `waybill.Total.TotalAmount = "700.00";` |
| 61   | `if (lineItem.LineItemNumber == 2)` | `if (lineItem.LineItemNumber == "2")` |
| 63   | `lineItem.LineItemNumber = 42;` | `lineItem.LineItemNumber = "42";` |

All other XML mutations in `Program.cs` (`Currency`, `Created`,
`DocumentID`, `Title`) are already string today and stay byte-identical.

### Tests

The change is breaking for every test that names a non-string scalar in a
generated (or hand-mirrored) XML model. The assertions take three shapes:

1. **Emitted source text contains `"long X" / "decimal X" / "bool X"`**
   (12 sites). Each rewrites to `"string X"`.
2. **`doc.X.Should().Be(<numeric>)` or `.BeTrue() / .BeFalse()`**
   (~13 sites). Each rewrites to `doc.X.Should().Be("<verbatim>")`.
3. **Mutating assignments `doc.X = <numeric>`** (~12 sites). Each
   rewrites to `doc.X = "<verbatim>"`.

#### Unit tests (`tests/Structura.UnitTests/Generator/`)

| File | Lines | Old → new |
|------|-------|-----------|
| `StructuraXmlGeneratorTests.cs` | 57, 58, 59, 85 | `"long Version" / "bool IsPriority" / "decimal TotalAmount" / "long Id"` → `"string Version" / "string IsPriority" / "string TotalAmount" / "string Id"` |
| `WrapperFlatteningTests.cs`     | 31, 45, 62 | `"long A" / "long X" / "long Id"` → `"string A" / "string X" / "string Id"` |
| `RepeatedElementsTests.cs`      | 121, 141, 142, 188, 208 | `"long Version" / "long Id" / "long Qty" / "long Version" / "long Id"` → `"string …"` |

`XmlNestedObjectsTests.cs`, `JsonModelEmitterTests.cs`,
`JsonHeterogeneousFieldsTests.cs`, `JsonNestedObjectsTests.cs`,
`JsonParseRootInfoTests.cs`, `JsonRepeatedElementsTests.cs`,
`StructuraJsonGeneratorTests.cs`, `RepeatedElementsTests.cs`'s JSON
cases, `ClassNameDeriverTests.cs`, `IdentifierSanitizerTests.cs`,
`JsonPointerEscaperTests.cs`, `StructuraDiagnosticsTests.cs` — untouched.

#### Unit tests (`tests/Structura.UnitTests/Xml/`)

`XmlValueWriterTests.cs` — delete every case that exercises `WriteInt64`,
`WriteInt32`, `WriteDouble`, `WriteDecimal`, `WriteBoolean`. Keep the
`WriteElementText` and `WriteAttributeValue` cases verbatim.

`XmlSourceParserTests.cs` — untouched.

#### Integration tests (`tests/Structura.IntegrationTests/Xml/`)

| File | Change |
|------|--------|
| `SmallOrderXml.cs` (hand-written fixture) | Rewrite to mirror new generator output: `long Version` → `string Version`, `bool IsPriority` → `string IsPriority`, drop `using System.Globalization` and `long.Parse` / `bool.Parse`, switch writers to `WriteElementText`. The fixture's purpose — documenting what the generator emits — survives the rewrite. |
| `SmallOrderXmlRoundTripTests.cs` | `order.Version = 42 / 99 / 42` → `"42" / "99" / "42"`; `order.IsPriority = false` (twice) → `"false"`. |
| `BlrwblSampleParseTests.cs` | `doc.SealID = 99999` (3 sites) → `"99999"`; assertions like `Should().Contain("<SealID>99999</SealID>")` are unchanged (they assert on the rendered XML, not on the C# type). |
| `BlrwblNestedObjectsTests.cs` | `Should().Be(4810987000544L)` etc. (6 GLN reads) → `Be("4810987000544")` etc.; `Should().Be(600.00m)` → `Be("600.00")`; `doc.Shipper.GLN = 9999988880001L` → `"9999988880001"`; `doc.Total.TotalAmount = 700.00m` → `"700.00"`. Containment assertions on rendered XML stay byte-identical. |
| `BlrwblLineItemTests.cs` | `long number = item.LineItemNumber` → `string number = item.LineItemNumber`; `LineItemNumber = 42 / 100`, `SealID = 99999` → string forms. Containment on rendered XML stays byte-identical. |
| `LibrarySampleParseTests.cs` | `library.Version.Should().Be(2.1m)` (the root `version="2.1"` attribute) → `Be("2.1")`. |
| `LibraryXmlNestedObjectsTests.cs` | `doc.Statistics.Available.Should().Be(3)` → `Be("3")`; `Verified.Should().BeTrue()` / `BeFalse()` → `Be("true")` / `Be("false")`. |

Reporting tests (`OrderSampleJsonReportingTests.cs`,
`OrderSampleJsonSideBySideDiffTests.cs`) and `SourceFileNameTests.cs`
are untouched — they don't read or assign XML scalar values.

JSON integration tests (`SmallOrder.cs`, `SmallOrderRoundTripTests.cs`,
`OrderSampleJsonGeneratedTests.cs`, etc.) are untouched.

## Diagnostics

No new diagnostic IDs. Existing STR-codes that report wrapped XML edge
cases stay the same. `InferKind`'s removal is invisible to the
diagnostics surface because the kind never appeared in a user-facing
message.

## Migration

There are no external clients (this is a single-host demo). The change
ships as one commit (or one PR with sub-commits per file group) on the
generator / runtime / demo / tests cut.

The breaking surface for in-repo callers is:
1. Generated property types flip from `long` / `decimal` / `bool` to
   `string` for affected paths.
2. Five `Program.cs` lines listed above.
3. The test rewrites above.

Anything else that compiles today and uses generated XML models must
have been hand-written against the previous typed property surface;
none such exists.

## Acceptance

1. `dotnet build Structura.slnx` clean — zero warnings, zero errors.
2. `dotnet test` green — both `Structura.UnitTests` and
   `Structura.IntegrationTests`.
3. `dotnet run --project src/Structura/Structura.csproj` (interactive
   terminal) produces:
   - the same byte-equal untouched regions of both XML samples as
     step 13 (the patch engine doesn't change);
   - the diff reporters show every mutated span exactly as today, with
     replacement text identical to before for `"99999"`, `"42"`,
     `"700.00"`, etc. (the writer for `string` outputs the value
     verbatim, just like the old `WriteInt64(99999)` did).
4. The generator's emitted source for `BlrwblSampleXml` and
   `LibrarySampleXml` no longer contains `long ` / `decimal ` / `bool `
   property declarations. (Spot check via `dotnet build -v:n` source
   dump, or via the existing emitter unit tests once they are updated.)

## Why this is safe wrt minimal-patch fidelity

The patch engine consumes `_ctx.Record(path, valueSpan, replacement)`.
Today's typed path is:

```
order.Price = 1250.75m;          // C# assignment
↓
_priceValueSpan, XmlValueWriter.WriteDecimal(1250.75m) → "1250.75"
↓
splice "1250.75" into the original byte span
```

After step 14 the path is:

```
order.Price = "1250.75";         // C# assignment
↓
_priceValueSpan, XmlValueWriter.WriteElementText("1250.75") → "1250.75"
                                                              (escapes only & < >)
↓
splice "1250.75" into the original byte span
```

The bytes spliced are identical for any value that wouldn't have
triggered `&`/`<`/`>` escaping under the typed writer (every numeric and
every boolean qualifies). For values that DO contain a special character
(`<`, `&`), the typed writer would have failed with `FormatException`
before; the string writer escapes correctly and the round-trip is well
defined. Net effect on existing samples: byte-equal output.

## Risks / open questions

- **Test surface is wider than the prod surface.** Most of the diff in
  this step is test rewrites — Tests table above enumerates them
  exhaustively as of this writing, but the implementer should re-grep
  before declaring the step done. The four sweeps that should return
  zero hits after the change:
  - `grep -rn 'long [A-Z]' tests/ src/`  (matches emitted `public long Year`
    style; expected JSON sites — `JsonGenScalarKind` cases — stay, so
    inspect by file).
  - `grep -rn 'decimal [A-Z]' tests/ src/`  (same caveat).
  - `grep -rn 'XmlGenScalarKind' .`  (must be zero).
  - `grep -rn 'WriteInt64\|WriteInt32\|WriteDouble\|WriteDecimal\|WriteBoolean' .`
    (must be zero outside `JsonValueWriter` — JSON still uses these).
- **`Program.cs` operator surprise.** `lineItem.LineItemNumber == 2`
  compares against an `int`/`long` today; rewritten as `== "2"` it's a
  string comparison. The semantics for the BLRWBL sample (`<LineItemNumber>2`)
  match because there's no leading whitespace or trailing zero — the
  byte-equality check `"2" == "2"` is the right thing. If a future
  sample writes `<LineItemNumber>02</LineItemNumber>` the comparison
  flips meaning, which is the correct behavior for a typeless surface.
- **Generated source comment.** The auto-generated header has no
  type hint, so no comment update is needed. Worth double-checking that
  no emitted XMLDoc summary mentions "as decimal" / "as long" anywhere
  in `XmlModelEmitter` (the current code emits no such summaries; the
  XMLDoc comments live on the runtime classes, not the generated ones).
- **Future re-introduction.** If we ever decide to add opt-in typed
  scalars (e.g. via a marker attribute on a partial-class property
  shadow), the schema model has to grow the `Kind` back. The cheapest
  way to keep that option open without compromising this step is to
  leave `XmlGenProperty` as a sealed class (not a record-struct) so
  adding a nullable `Kind?` field later is non-breaking. No need to
  pre-bake the API surface now.
