# Step 14 — XML scalars as `string` (Implementation Plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Flip every generated XML scalar property to `string`, then prune the dead type-inference and numeric-writer code paths. JSON is untouched.

**Architecture:** Two commits. Commit 1 co-changes the XML emitter + every test that asserted a non-string XML type + the demo (this is one logical unit because flipping the emitter breaks every downstream caller, and the build can only be green after all callers are updated). Commit 2 deletes the now-unreachable `XmlGenScalarKind` enum, `InferKind`, numeric `XmlValueWriter` methods, and their tests.

**Tech Stack:** .NET 10, Roslyn source generators (netstandard2.0), xUnit, FluentAssertions.

**Spec:** `docs/plans/step-14-xml-scalars-as-string.md`

---

## File structure

Files modified in **Task 1** (flip + co-fix):

| Path | Role |
|------|------|
| `src/Structura.Generator/XmlModelEmitter.cs` | Force string-only bindings in the three `Build*Scalar` builders. |
| `tests/Structura.UnitTests/Generator/StructuraXmlGeneratorTests.cs` | Update 4 assertions (lines 57–59, 85). |
| `tests/Structura.UnitTests/Generator/WrapperFlatteningTests.cs` | Update 3 assertions (lines 31, 45, 62). |
| `tests/Structura.UnitTests/Generator/RepeatedElementsTests.cs` | Update 5 assertions (lines 121, 141, 142, 188, 208). |
| `tests/Structura.IntegrationTests/Xml/SmallOrderXml.cs` | Rewrite hand-written fixture: `string Version` + `string IsPriority`. |
| `tests/Structura.IntegrationTests/Xml/SmallOrderXmlRoundTripTests.cs` | Switch assignments to string literals. |
| `tests/Structura.IntegrationTests/Xml/BlrwblSampleParseTests.cs` | `doc.SealID = 99999` → `"99999"` (3 sites). |
| `tests/Structura.IntegrationTests/Xml/BlrwblNestedObjectsTests.cs` | Numeric `Should().Be(...)` + assignments → string forms. |
| `tests/Structura.IntegrationTests/Xml/BlrwblLineItemTests.cs` | `long number = …` and numeric assignments → string forms. |
| `tests/Structura.IntegrationTests/Xml/LibrarySampleParseTests.cs` | `.Version.Should().Be(2.1m)` → `Be("2.1")`. |
| `tests/Structura.IntegrationTests/Xml/LibraryXmlNestedObjectsTests.cs` | `Available.Should().Be(3)` → `Be("3")`; `Verified.Should().BeTrue/BeFalse()` → `Be("true")` / `Be("false")`. |
| `src/Structura/Program.cs` | 5 lines (54, 56, 57, 61, 63). |

Files modified in **Task 2** (cleanup):

| Path | Role |
|------|------|
| `src/Structura.Generator/XmlGenSchema.cs` | Delete `enum XmlGenScalarKind`. Drop `Kind` from `XmlGenProperty`. |
| `src/Structura.Generator/GeneratorXmlParser.cs` | Delete `InferKind` method. Drop `XmlGenScalarKind.X` arguments from `new XmlGenProperty(...)` calls. Drop `using System.Globalization` if unused. |
| `src/Structura.Generator/XmlModelEmitter.cs` | Delete now-unused `Kind` field on `ItemScalar`, simplify constructors. Drop generated `using System.Globalization;` line. |
| `src/Structura.Runtime/Xml/XmlValueWriter.cs` | Delete `WriteInt64`, `WriteInt32`, `WriteDouble`, `WriteDecimal`, `WriteBoolean`. Drop `using System.Globalization`. |
| `tests/Structura.UnitTests/Xml/XmlValueWriterTests.cs` | Delete every test case that exercises the deleted methods. |

---

## Task 1: Flip XML scalar emission to `string` (one commit)

**Files:** see Task 1 table above.

### Step 1.1 — Read the current state

- [ ] **Read** `src/Structura.Generator/XmlModelEmitter.cs` lines 974–1106. Confirm the three scalar builders (`BuildRootScalar`, `BuildItemScalar`, `BuildNestedScalar`) all route through `AttributeBindings` and `ElementBindings`. Note exact method signatures.

### Step 1.2 — Update the unit test that asserts emitted property types (RED)

- [ ] **Edit** `tests/Structura.UnitTests/Generator/StructuraXmlGeneratorTests.cs` lines 56–59:

  Old:
  ```csharp
          source.Should().Contain("string Currency");
          source.Should().Contain("long Version");
          source.Should().Contain("bool IsPriority");
          source.Should().Contain("decimal TotalAmount");
  ```
  New:
  ```csharp
          source.Should().Contain("string Currency");
          source.Should().Contain("string Version");
          source.Should().Contain("string IsPriority");
          source.Should().Contain("string TotalAmount");
          source.Should().NotContain("long Version");
          source.Should().NotContain("bool IsPriority");
          source.Should().NotContain("decimal TotalAmount");
  ```

  The two `NotContain` lines are a guard rail against accidental partial inference (e.g. if someone re-introduces typing for one specific case). They cost one line each in source size.

- [ ] **Edit** the same file line 85:

  Old: `source.Should().Contain("long Id");`
  New: `source.Should().Contain("string Id");`

- [ ] **Run** `dotnet test tests/Structura.UnitTests/Structura.UnitTests.csproj --filter "FullyQualifiedName~StructuraXmlGeneratorTests.Generator_EmitsScalarProperty"` and verify both rewritten tests FAIL with the current emitter (emitter still produces `long Version` etc., so `Contain("string Version")` fails).

### Step 1.3 — Flip the emitter to emit `string` for every XML scalar (GREEN)

- [ ] **Edit** `src/Structura.Generator/XmlModelEmitter.cs`:

  In `BuildRootScalar` (currently around line 976):

  Old (attribute branch, lines ~982–995):
  ```csharp
              (string csharpType, string ctorExpr, string writerCall) = AttributeBindings(prop.Kind, camelName + "Attr");
              return new RootScalar(
                  xmlName: prop.Name,
                  pascalName: pascalName,
                  camelName: camelName,
                  csharpType: csharpType,
                  lookupExpr: $"root.RequireAttribute(\"{EscapeForCsString(prop.Name)}\")",
                  lookupVarName: camelName + "Attr",
                  spanExpr: ".ValueSpan",
                  ctorExpr: ctorExpr,
                  writerCall: writerCall,
                  path: "/@" + prop.Name);
  ```
  New:
  ```csharp
              return new RootScalar(
                  xmlName: prop.Name,
                  pascalName: pascalName,
                  camelName: camelName,
                  csharpType: "string",
                  lookupExpr: $"root.RequireAttribute(\"{EscapeForCsString(prop.Name)}\")",
                  lookupVarName: camelName + "Attr",
                  spanExpr: ".ValueSpan",
                  ctorExpr: $"{camelName}Attr.Value",
                  writerCall: "XmlValueWriter.WriteAttributeValue(value)",
                  path: "/@" + prop.Name);
  ```

  Old (element branch, lines ~997–1009):
  ```csharp
          (string typeStr, string ctor, string writer) = ElementBindings(prop.Kind, $"((XmlSourceText){camelName}El.Children[0]).Value");
          return new RootScalar(
              xmlName: prop.Name,
              pascalName: pascalName,
              camelName: camelName,
              csharpType: typeStr,
              lookupExpr: $"root.RequireElement(\"{EscapeForCsString(prop.Name)}\")",
              lookupVarName: camelName + "El",
              spanExpr: ".InnerSpan",
              ctorExpr: ctor,
              writerCall: writer,
              path: "/" + pascalName);
  ```
  New:
  ```csharp
          return new RootScalar(
              xmlName: prop.Name,
              pascalName: pascalName,
              camelName: camelName,
              csharpType: "string",
              lookupExpr: $"root.RequireElement(\"{EscapeForCsString(prop.Name)}\")",
              lookupVarName: camelName + "El",
              spanExpr: ".InnerSpan",
              ctorExpr: $"((XmlSourceText){camelName}El.Children[0]).Value",
              writerCall: "XmlValueWriter.WriteElementText(value)",
              path: "/" + pascalName);
  ```

  In `BuildItemScalar` (currently around line 1011):

  Old (attribute branch, lines ~1016–1029):
  ```csharp
          if (prop.IsAttribute)
          {
              (string csharpType, string ctorExpr, string writerCall) = AttributeBindings(prop.Kind, camelName + "Attr");
              return new ItemScalar(
                  xmlName: prop.Name,
                  pascalName: pascalName,
                  camelName: camelName,
                  csharpType: csharpType,
                  isAttribute: true,
                  lookupVarName: camelName + "Attr",
                  absentSafeCtorExpr: ctorExpr,
                  writerCall: writerCall,
                  kind: prop.Kind);
          }
  ```
  New:
  ```csharp
          if (prop.IsAttribute)
          {
              return new ItemScalar(
                  xmlName: prop.Name,
                  pascalName: pascalName,
                  camelName: camelName,
                  csharpType: "string",
                  isAttribute: true,
                  lookupVarName: camelName + "Attr",
                  absentSafeCtorExpr: $"{camelName}Attr.Value",
                  writerCall: "XmlValueWriter.WriteAttributeValue(value)");
          }
  ```

  Old (element branch, lines ~1031–1043):
  ```csharp
          var textExpr = $"({camelName}El.Children.Count > 0 && {camelName}El.Children[0] is XmlSourceText t_{camelName} ? t_{camelName}.Value : string.Empty)";
          (string typeStr, string ctor, string writer) = ElementBindings(prop.Kind, textExpr);
          return new ItemScalar(
              xmlName: prop.Name,
              pascalName: pascalName,
              camelName: camelName,
              csharpType: typeStr,
              isAttribute: false,
              lookupVarName: camelName + "El",
              absentSafeCtorExpr: ctor,
              writerCall: writer,
              kind: prop.Kind);
  ```
  New:
  ```csharp
          var textExpr = $"({camelName}El.Children.Count > 0 && {camelName}El.Children[0] is XmlSourceText t_{camelName} ? t_{camelName}.Value : string.Empty)";
          return new ItemScalar(
              xmlName: prop.Name,
              pascalName: pascalName,
              camelName: camelName,
              csharpType: "string",
              isAttribute: false,
              lookupVarName: camelName + "El",
              absentSafeCtorExpr: textExpr,
              writerCall: "XmlValueWriter.WriteElementText(value)");
  ```

  In `BuildNestedScalar` (currently around line 799):

  Old (attribute branch, lines ~803–817):
  ```csharp
          if (prop.IsAttribute)
          {
              (string csharpType, string ctorExpr, string writerCall) = AttributeBindings(prop.Kind, camelName + "Attr");
              return new NestedScalar(
                  xmlName: prop.Name,
                  pascalName: pascalName,
                  camelName: camelName,
                  csharpType: csharpType,
                  isAttribute: true,
                  lookupVarName: camelName + "Attr",
                  ctorExpr: ctorExpr,
                  writerCall: writerCall,
                  pathSuffix: "/@" + prop.Name);
          }
  ```
  New:
  ```csharp
          if (prop.IsAttribute)
          {
              return new NestedScalar(
                  xmlName: prop.Name,
                  pascalName: pascalName,
                  camelName: camelName,
                  csharpType: "string",
                  isAttribute: true,
                  lookupVarName: camelName + "Attr",
                  ctorExpr: $"{camelName}Attr.Value",
                  writerCall: "XmlValueWriter.WriteAttributeValue(value)",
                  pathSuffix: "/@" + prop.Name);
          }
  ```

  Old (element branch, lines ~819–834):
  ```csharp
          // Match the safe-on-empty pattern from BuildItemScalar so empty
          // elements (<last-name></last-name>) read as empty strings rather
          // than crashing on Children[0].
          var textExpr = $"({camelName}El.Children.Count > 0 && {camelName}El.Children[0] is XmlSourceText t_{camelName} ? t_{camelName}.Value : string.Empty)";
          (string typeStr, string ctor, string writer) = ElementBindings(prop.Kind, textExpr);
          return new NestedScalar(
              xmlName: prop.Name,
              pascalName: pascalName,
              camelName: camelName,
              csharpType: typeStr,
              isAttribute: false,
              lookupVarName: camelName + "El",
              ctorExpr: ctor,
              writerCall: writer,
              pathSuffix: "/" + pascalName);
  ```
  New:
  ```csharp
          // Match the safe-on-empty pattern from BuildItemScalar so empty
          // elements (<last-name></last-name>) read as empty strings rather
          // than crashing on Children[0].
          var textExpr = $"({camelName}El.Children.Count > 0 && {camelName}El.Children[0] is XmlSourceText t_{camelName} ? t_{camelName}.Value : string.Empty)";
          return new NestedScalar(
              xmlName: prop.Name,
              pascalName: pascalName,
              camelName: camelName,
              csharpType: "string",
              isAttribute: false,
              lookupVarName: camelName + "El",
              ctorExpr: textExpr,
              writerCall: "XmlValueWriter.WriteElementText(value)",
              pathSuffix: "/" + pascalName);
  ```

  In the `ItemScalar` record (currently around line 1207–1240):

  Drop the `Kind` constructor parameter and `Kind` property. Old constructor:
  ```csharp
          public ItemScalar(
              string xmlName,
              string pascalName,
              string camelName,
              string csharpType,
              bool isAttribute,
              string lookupVarName,
              string absentSafeCtorExpr,
              string writerCall,
              XmlGenScalarKind kind)
  ```
  New:
  ```csharp
          public ItemScalar(
              string xmlName,
              string pascalName,
              string camelName,
              string csharpType,
              bool isAttribute,
              string lookupVarName,
              string absentSafeCtorExpr,
              string writerCall)
  ```
  Drop the `Kind` property and the constructor body line that assigns it.

  In `EmitItemScalarCtor` (currently around line 686, two call sites at lines ~700–702 and ~715–717):

  Old:
  ```csharp
              sb.Append("            _").Append(s.CamelName).Append(" = ")
                .Append(s.LookupVarName).Append(" is null ? ")
                .Append(DefaultValueExpr(s.Kind)).Append(" : ")
                .Append(s.AbsentSafeCtorExpr).AppendLine(";");
  ```
  New (both sites):
  ```csharp
              sb.Append("            _").Append(s.CamelName).Append(" = ")
                .Append(s.LookupVarName).Append(" is null ? string.Empty : ")
                .Append(s.AbsentSafeCtorExpr).AppendLine(";");
  ```

  In the file header that the emitter writes for the generated source (currently around line 28):

  Old:
  ```csharp
          sb.AppendLine("using System.Globalization;");
  ```
  New: delete that line entirely. The generated source no longer calls `long.Parse(..., CultureInfo.InvariantCulture)` etc., so `CultureInfo` is no longer referenced.

  Note: `AttributeBindings`, `ElementBindings`, and `DefaultValueExpr` are now unreachable. **Leave them in place for now** (Task 2 deletes them). Build cleanliness through Task 1 only requires zero callers.

- [ ] **Run** `dotnet build src/Structura.Generator/Structura.Generator.csproj`. Expected: clean.

### Step 1.4 — Run the rewritten unit test to verify GREEN

- [ ] **Run** `dotnet test tests/Structura.UnitTests/Structura.UnitTests.csproj --filter "FullyQualifiedName~StructuraXmlGeneratorTests.Generator_EmitsScalarProperty"`. Expected: PASS.

### Step 1.5 — Fix the remaining unit-test type assertions

- [ ] **Edit** `tests/Structura.UnitTests/Generator/WrapperFlatteningTests.cs` lines 31, 45, 62:

  Old (3 distinct sites):
  ```csharp
          source.Should().Contain("long A");
          ...
          source.Should().Contain("long X");
          ...
          source.Should().Contain("long Id");
  ```
  New (each):
  ```csharp
          source.Should().Contain("string A");
          ...
          source.Should().Contain("string X");
          ...
          source.Should().Contain("string Id");
  ```

- [ ] **Edit** `tests/Structura.UnitTests/Generator/RepeatedElementsTests.cs` lines 121, 141, 142, 188, 208:

  Old:
  ```csharp
          source.Should().Contain("long Version");   // line 121
          ...
          source.Should().Contain("long Id");        // line 141
          source.Should().Contain("long Qty");       // line 142
          ...
          source.Should().Contain("long Version");   // line 188
          ...
          source.Should().Contain("long Id");        // line 208
  ```
  New: every `"long X"` → `"string X"`.

- [ ] **Run** `dotnet test tests/Structura.UnitTests/Structura.UnitTests.csproj`. Expected: every test PASS.

### Step 1.6 — Rewrite the hand-written `SmallOrderXml` fixture

- [ ] **Edit** `tests/Structura.IntegrationTests/Xml/SmallOrderXml.cs` — replace the entire file with:

  ```csharp
  using Structura.Runtime;
  using Structura.Runtime.Xml;

  namespace Structura.IntegrationTests.Xml;

  /// <summary>
  /// Hand-written XML stand-in for a generator-produced model. Mirrors
  /// <c>SmallOrder</c> (JSON) — same StructuraDocumentContext, same dirty
  /// tracking pattern, same explicit <see cref="IStructuraDocument"/>
  /// implementation forwarding to the context — but each property's
  /// patchable region is the element's <see cref="XmlSourceElement.InnerSpan"/>
  /// (i.e. the bytes between <c>&gt;</c> and <c>&lt;/…&gt;</c>).
  /// </summary>
  public sealed class SmallOrderXml : IStructuraXmlDocument<SmallOrderXml>, IStructuraDocument
  {
      private readonly StructuraDocumentContext _ctx;

      private readonly TextSpan _currencyValueSpan;
      private readonly TextSpan _versionValueSpan;
      private readonly TextSpan _isPriorityValueSpan;

      private string _currency;
      private string _version;
      private string _isPriority;

      private SmallOrderXml(string source, XmlSourceElement root)
      {
          _ctx = new StructuraDocumentContext(source, SourceFileName);

          XmlSourceElement currency = root.RequireElement("currency");
          _currencyValueSpan = currency.InnerSpan;
          _currency = ((XmlSourceText)currency.Children[0]).Value;

          XmlSourceElement version = root.RequireElement("version");
          _versionValueSpan = version.InnerSpan;
          _version = ((XmlSourceText)version.Children[0]).Value;

          XmlSourceElement isPriority = root.RequireElement("is_priority");
          _isPriorityValueSpan = isPriority.InnerSpan;
          _isPriority = ((XmlSourceText)isPriority.Children[0]).Value;
      }

      public static string SourceFileName => "small-order.xml";

      public static SmallOrderXml ParseFromXml(string source)
      {
          XmlSourceElement root = XmlSourceParser.Parse(source);
          if (!string.Equals(root.Name, "order", StringComparison.Ordinal))
          {
              throw new XmlParseException(
                  $"Expected <order> at root, found <{root.Name}>.");
          }
          return new SmallOrderXml(source, root);
      }

      public string Currency
      {
          get => _currency;
          set
          {
              _currency = value;
              _ctx.Record("/currency", _currencyValueSpan, XmlValueWriter.WriteElementText(value));
          }
      }

      public string Version
      {
          get => _version;
          set
          {
              _version = value;
              _ctx.Record("/version", _versionValueSpan, XmlValueWriter.WriteElementText(value));
          }
      }

      public string IsPriority
      {
          get => _isPriority;
          set
          {
              _isPriority = value;
              _ctx.Record("/is_priority", _isPriorityValueSpan, XmlValueWriter.WriteElementText(value));
          }
      }

      public string ToXml()
      {
          return _ctx.ApplyEdits();
      }

      string IStructuraDocument.OriginalText => _ctx.OriginalText;
      string IStructuraDocument.CurrentText => _ctx.ApplyEdits();
      string IStructuraDocument.DocumentName => _ctx.DocumentName;
      IReadOnlyList<DocumentChange> IStructuraDocument.Changes => _ctx.Changes;
  }
  ```

  Key changes vs. the old file: `_version` is `string` (was `long`); `_isPriority` is `string` (was `bool`); all three constructors read `((XmlSourceText)el.Children[0]).Value` directly with no `Parse`; all three setters call `WriteElementText`; the `using System.Globalization;` line is gone.

### Step 1.7 — Fix `SmallOrderXmlRoundTripTests`

- [ ] **Edit** `tests/Structura.IntegrationTests/Xml/SmallOrderXmlRoundTripTests.cs` lines 47, 62, 78, 79, 118:

  Old (5 sites):
  ```csharp
          order.Version = 42;       // line 47
          order.IsPriority = false; // line 62
          order.Version = 99;       // line 78
          order.IsPriority = false; // line 79
          order.Version = 42;       // line 118
  ```
  New:
  ```csharp
          order.Version = "42";
          order.IsPriority = "false";
          order.Version = "99";
          order.IsPriority = "false";
          order.Version = "42";
  ```

  If any test in this file ALSO reads `order.Version.Should().Be(42)` or similar, switch those to `Be("42")` too. Skim the whole file for numeric/boolean comparisons.

### Step 1.8 — Fix `BlrwblSampleParseTests`

- [ ] **Edit** `tests/Structura.IntegrationTests/Xml/BlrwblSampleParseTests.cs` lines 147, 157, 174:

  Old (3 sites):
  ```csharp
          doc.SealID = 99999;   // line 147
          doc.SealID = 99999;   // line 157
          doc.SealID = 99999;   // line 174
  ```
  New:
  ```csharp
          doc.SealID = "99999";
          doc.SealID = "99999";
          doc.SealID = "99999";
  ```

  Also on line 176:
  ```csharp
          doc.DespatchAdviceLogisticUnitLineItem.LineItems[1].LineItemNumber = 42;
  ```
  →
  ```csharp
          doc.DespatchAdviceLogisticUnitLineItem.LineItems[1].LineItemNumber = "42";
  ```

  Assertions like `Should().Contain("<SealID>99999</SealID>")` on the rendered XML are unchanged (they assert on the resulting text, not on the C# type).

### Step 1.9 — Fix `BlrwblNestedObjectsTests`

- [ ] **Edit** `tests/Structura.IntegrationTests/Xml/BlrwblNestedObjectsTests.cs`:

  Lines 41, 52, 62, 71, 80, 88 (numeric GLN reads):
  Old:
  ```csharp
          doc.Shipper.GLN.Should().Be(4810987000544L);
          doc.Receiver.GLN.Should().Be(4810117000635L);
          doc.FreightPayer.GLN.Should().Be(4812409900009L);
          doc.ShipFrom.GLN.Should().Be(4810989000009L);
          doc.ShipTo.GLN.Should().Be(4810047000002L);
          doc.Transporter.GLN.Should().Be(4812409900009L);
  ```
  New:
  ```csharp
          doc.Shipper.GLN.Should().Be("4810987000544");
          doc.Receiver.GLN.Should().Be("4810117000635");
          doc.FreightPayer.GLN.Should().Be("4812409900009");
          doc.ShipFrom.GLN.Should().Be("4810989000009");
          doc.ShipTo.GLN.Should().Be("4810047000002");
          doc.Transporter.GLN.Should().Be("4812409900009");
  ```

  Line 108: `doc.Total.TotalAmount.Should().Be(600.00m);` → `Be("600.00");`

  Lines 119, 131, 143, 145 (assignments):
  Old:
  ```csharp
          doc.Shipper.GLN = 9999988880001L;
          doc.Total.TotalAmount = 700.00m;
          doc.Total.TotalAmount = 700.00m;
          doc.Shipper.GLN = 9999988880001L;
  ```
  New:
  ```csharp
          doc.Shipper.GLN = "9999988880001";
          doc.Total.TotalAmount = "700.00";
          doc.Total.TotalAmount = "700.00";
          doc.Shipper.GLN = "9999988880001";
  ```

  Containment assertions on rendered XML (`Contain("<TotalAmount>700.00</TotalAmount>")` etc.) are unchanged.

### Step 1.10 — Fix `BlrwblLineItemTests`

- [ ] **Edit** `tests/Structura.IntegrationTests/Xml/BlrwblLineItemTests.cs`:

  Line 38: `long number = item.LineItemNumber;` → `string number = item.LineItemNumber;`
  Line 75: `doc.DespatchAdviceLogisticUnitLineItem.LineItems[1].LineItemNumber = 42;` → `= "42";`
  Line 124: `doc.SealID = 99999;` → `= "99999";`
  Line 125: `doc.DespatchAdviceLogisticUnitLineItem.LineItems[0].LineItemNumber = 100;` → `= "100";`

  Also: if `number` (the `long` local on line 38) is subsequently used in a comparison or arithmetic operation, switch those to string operations or use a separate parsed local. Skim lines 38–60 to confirm.

### Step 1.11 — Fix `LibrarySampleParseTests`

- [ ] **Edit** `tests/Structura.IntegrationTests/Xml/LibrarySampleParseTests.cs` line 36:

  Old: `library.Version.Should().Be(2.1m);`
  New: `library.Version.Should().Be("2.1");`

  (`Version` here is the root `<library version="2.1" …>` attribute.)

### Step 1.12 — Fix `LibraryXmlNestedObjectsTests`

- [ ] **Edit** `tests/Structura.IntegrationTests/Xml/LibraryXmlNestedObjectsTests.cs`:

  Line 54: `doc.Statistics.Available.Should().Be(3);` → `Be("3");`
  Line 104: `b005Reviews[0].Reviewer.Verified.Should().BeTrue();` → `Be("true");`
  Line 106: `b005Reviews[1].Reviewer.Verified.Should().BeFalse();` → `Be("false");`

  Skim the rest of the file for any numeric `Be(...)` or `BeTrue/BeFalse` on XML properties — there may be other numeric reads (e.g. `Total`, year-like elements). If found, switch to string `Be(...)`.

### Step 1.13 — Fix `Program.cs`

- [ ] **Edit** `src/Structura/Program.cs`:

  Line 54: `waybill.SealID = 99999;` → `waybill.SealID = "99999";`
  Line 56: `waybill.Shipper.GLN = 9999988880001L;` → `waybill.Shipper.GLN = "9999988880001";`
  Line 57: `waybill.Total.TotalAmount = 700.00m;` → `waybill.Total.TotalAmount = "700.00";`
  Line 61: `if (lineItem.LineItemNumber == 2)` → `if (lineItem.LineItemNumber == "2")`
  Line 63: `lineItem.LineItemNumber = 42;` → `lineItem.LineItemNumber = "42";`

### Step 1.14 — Full build + test sweep

- [ ] **Run** `dotnet build Structura.slnx`. Expected: clean, zero warnings, zero errors.
- [ ] **Run** `dotnet test`. Expected: every test PASS in both `Structura.UnitTests` and `Structura.IntegrationTests`.

### Step 1.15 — Eyeball demo

- [ ] **Run** `dotnet run --project src/Structura/Structura.csproj`. Expected:
  - All four pipelines (order JSON, BLRWBL XML, library XML, library JSON) print without exception.
  - The BLRWBL diff shows `<SealID>99999</SealID>`, `<TotalAmount>700.00</TotalAmount>`, `<LineItemNumber>42</LineItemNumber>` substitutions exactly as before — byte-equal to step-13 output.
  - JSON diffs are byte-equal (JSON is untouched).

### Step 1.16 — Commit

- [ ] **Run**:
  ```bash
  git add src/Structura.Generator/XmlModelEmitter.cs \
          tests/Structura.UnitTests/Generator/StructuraXmlGeneratorTests.cs \
          tests/Structura.UnitTests/Generator/WrapperFlatteningTests.cs \
          tests/Structura.UnitTests/Generator/RepeatedElementsTests.cs \
          tests/Structura.IntegrationTests/Xml/SmallOrderXml.cs \
          tests/Structura.IntegrationTests/Xml/SmallOrderXmlRoundTripTests.cs \
          tests/Structura.IntegrationTests/Xml/BlrwblSampleParseTests.cs \
          tests/Structura.IntegrationTests/Xml/BlrwblNestedObjectsTests.cs \
          tests/Structura.IntegrationTests/Xml/BlrwblLineItemTests.cs \
          tests/Structura.IntegrationTests/Xml/LibrarySampleParseTests.cs \
          tests/Structura.IntegrationTests/Xml/LibraryXmlNestedObjectsTests.cs \
          src/Structura/Program.cs
  git commit -m "feat(generator): emit XML scalars as string instead of inferring types

  XML carries no syntactic type information; one-shot inference on the
  first sample value is fragile. Every generated XML scalar property is
  now string regardless of whether the sample looks like an int, decimal,
  or boolean. JSON inference is unaffected.

  See docs/plans/step-14-xml-scalars-as-string.md."
  ```

---

## Task 2: Prune dead code (one commit)

**Files:** see Task 2 table above.

### Step 2.1 — Confirm dead code via greps (before deleting anything)

- [ ] **Run** `grep -rn "XmlGenScalarKind" src/ tests/`. Expected: zero hits in `src/` other than the enum definition in `XmlGenSchema.cs` and the (now-unreferenced) parameters / properties in `XmlGenProperty`, `ItemScalar`, `AttributeBindings`, `ElementBindings`, `DefaultValueExpr`, `InferKind`. Zero hits in `tests/`.

  If `grep` finds a real consumer outside this scope, STOP and surface the finding before deleting.

### Step 2.2 — Delete `XmlGenScalarKind` enum + `Kind` field on `XmlGenProperty`

- [ ] **Edit** `src/Structura.Generator/XmlGenSchema.cs` lines 5–29:

  Old:
  ```csharp
  // ── Property kind discriminated union ────────────────────────────────────────

  internal enum XmlGenScalarKind
  {
      String,
      Long,
      Decimal,
      Boolean,
  }

  // ── Scalar property ──────────────────────────────────────────────────────────

  internal sealed class XmlGenProperty
  {
      public XmlGenProperty(string name, XmlGenScalarKind kind, bool isAttribute)
      {
          Name = name;
          Kind = kind;
          IsAttribute = isAttribute;
      }

      public string Name { get; }
      public XmlGenScalarKind Kind { get; }
      public bool IsAttribute { get; }
  }
  ```
  New:
  ```csharp
  // ── Scalar property ──────────────────────────────────────────────────────────

  internal sealed class XmlGenProperty
  {
      public XmlGenProperty(string name, bool isAttribute)
      {
          Name = name;
          IsAttribute = isAttribute;
      }

      public string Name { get; }
      public bool IsAttribute { get; }
  }
  ```

### Step 2.3 — Fix `GeneratorXmlParser` call sites + delete `InferKind`

- [ ] **Edit** `src/Structura.Generator/GeneratorXmlParser.cs`:

  Line 724 (in `ClassifyChild`, self-closing branch):

  Old:
  ```csharp
                  result.PureTextScalar = new XmlGenProperty(name, XmlGenScalarKind.String, isAttribute: false);
  ```
  New:
  ```csharp
                  result.PureTextScalar = new XmlGenProperty(name, isAttribute: false);
  ```

  Line 796–798 (in `ClassifyChild`, pure-text branch):

  Old:
  ```csharp
              string rawText = xml.Substring(contentStart, contentEnd - contentStart);
              string decoded = DecodeEntities(rawText, obs);
              result.PureTextScalar = new XmlGenProperty(name, InferKind(decoded), isAttribute: false);
  ```
  New (still calls `DecodeEntities` for its side effects on `obs.SawUnknownEntity` / `obs.FirstUnknownEntityName`):
  ```csharp
              string rawText = xml.Substring(contentStart, contentEnd - contentStart);
              _ = DecodeEntities(rawText, obs);
              result.PureTextScalar = new XmlGenProperty(name, isAttribute: false);
  ```

  Line 833–840 (in `ReadAttribute`):

  Old:
  ```csharp
          string raw = xml.Substring(valStart, valEnd - valStart);
          p = valEnd + 1;
          string decoded = DecodeEntities(raw, obs);
          if (IsXmlnsAttribute(name))
          {
              obs.SawNamespaceDecl = true;
          }
          return new XmlGenProperty(name, InferKind(decoded), isAttribute: true);
  ```
  New:
  ```csharp
          string raw = xml.Substring(valStart, valEnd - valStart);
          p = valEnd + 1;
          _ = DecodeEntities(raw, obs);
          if (IsXmlnsAttribute(name))
          {
              obs.SawNamespaceDecl = true;
          }
          return new XmlGenProperty(name, isAttribute: true);
  ```

  Lines 1070–1092 (the entire `InferKind` method): delete.

  Top of file (line 4): the `using System.Globalization;` is used by `int.TryParse(..., NumberStyles.HexNumber, CultureInfo.InvariantCulture, …)` and similar inside `DecodeEntities`. Confirm by inspecting the file after the InferKind delete — leave the using if `DecodeEntities` still references `CultureInfo`/`NumberStyles`; delete the using if `DecodeEntities` was its only other consumer. (As of this writing `DecodeEntities` uses `NumberStyles.HexNumber` and `CultureInfo.InvariantCulture` for numeric-entity decoding, so the using STAYS.)

### Step 2.4 — Delete unreachable binding helpers in `XmlModelEmitter`

- [ ] **Edit** `src/Structura.Generator/XmlModelEmitter.cs`:

  Delete entire methods:
  - `AttributeBindings` (currently lines ~1045–1068)
  - `ElementBindings` (currently lines ~1070–1093)
  - `DefaultValueExpr` (currently lines ~1095–1106)

  Drop the `Kind` references from the `ItemScalar` record. Task 1 already removed the constructor parameter and Kind property; double-check no leftover `s.Kind` / `prop.Kind` references remain. Grep:
  ```bash
  grep -n "\.Kind\b" src/Structura.Generator/XmlModelEmitter.cs
  ```
  Expected: zero hits.

  The top-of-file `using` block in the emitter (lines 1–6) — leave alone. The emitter itself still uses `System.Text` for `StringBuilder` and `Microsoft.CodeAnalysis.CSharp` for `SymbolDisplay`. `System.Globalization` is NOT imported by the emitter (only by the generated source it writes — and Task 1 already removed that line from the emitted header).

### Step 2.5 — Delete numeric `XmlValueWriter` methods

- [ ] **Edit** `src/Structura.Runtime/Xml/XmlValueWriter.cs`:

  Delete lines 84–109:
  ```csharp
      public static string WriteInt64(long value)
      {
          return value.ToString(CultureInfo.InvariantCulture);
      }

      public static string WriteInt32(int value)
      {
          return value.ToString(CultureInfo.InvariantCulture);
      }

      public static string WriteDouble(double value)
      {
          return value.ToString("R", CultureInfo.InvariantCulture);
      }

      public static string WriteDecimal(decimal value)
      {
          return value.ToString(CultureInfo.InvariantCulture);
      }

      public static string WriteBoolean(bool value)
      {
          return value
              ? "true"
              : "false";
      }
  ```

  Delete the `using System.Globalization;` line at the top (line 1) — `WriteElementText` and `WriteAttributeValue` don't reference `CultureInfo`. Confirm by inspecting the file post-edit; if anything in the remaining file references `CultureInfo`, leave the using.

### Step 2.6 — Delete `XmlValueWriterTests` cases for the removed writers

- [ ] **Edit** `tests/Structura.UnitTests/Xml/XmlValueWriterTests.cs`:

  Delete every `[Fact]` / `[Theory]` that exercises `WriteInt64`, `WriteInt32`, `WriteDouble`, `WriteDecimal`, or `WriteBoolean`. Keep every test that exercises `WriteElementText` or `WriteAttributeValue` byte-identical.

  Quick check after edit:
  ```bash
  grep -n "WriteInt64\|WriteInt32\|WriteDouble\|WriteDecimal\|WriteBoolean" tests/Structura.UnitTests/Xml/XmlValueWriterTests.cs
  ```
  Expected: zero hits.

### Step 2.7 — Final greps (correctness sweep from spec's Risks section)

- [ ] **Run** `grep -rn "XmlGenScalarKind" .`. Expected: zero hits anywhere.
- [ ] **Run** `grep -rn "WriteInt64\|WriteInt32\|WriteDouble\|WriteDecimal\|WriteBoolean" src/Structura.Runtime/Xml/ src/Structura.Generator/ tests/Structura.UnitTests/Xml/ src/Structura/`. Expected: zero hits. (Hits inside `src/Structura.Runtime/Json/JsonValueWriter.cs` and JSON-side generator/tests are expected — JSON is untouched.)
- [ ] **Run** `grep -rn "InferKind" src/ tests/`. Expected: zero hits.

### Step 2.8 — Build + test + run sweep

- [ ] **Run** `dotnet build Structura.slnx`. Expected: clean.
- [ ] **Run** `dotnet test`. Expected: every test PASS.
- [ ] **Run** `dotnet run --project src/Structura/Structura.csproj`. Expected: same byte-equal output as the end of Task 1 (the cleanup commit changes no runtime behavior).

### Step 2.9 — Commit

- [ ] **Run**:
  ```bash
  git add src/Structura.Generator/XmlGenSchema.cs \
          src/Structura.Generator/GeneratorXmlParser.cs \
          src/Structura.Generator/XmlModelEmitter.cs \
          src/Structura.Runtime/Xml/XmlValueWriter.cs \
          tests/Structura.UnitTests/Xml/XmlValueWriterTests.cs
  git commit -m "refactor(generator,runtime): drop XML type-inference and numeric value writers

  Unreachable after the XML-scalars-as-string flip:
  - XmlGenScalarKind enum + Kind property on XmlGenProperty.
  - GeneratorXmlParser.InferKind.
  - XmlValueWriter.WriteInt64/Int32/Double/Decimal/Boolean.
  - The corresponding test cases in XmlValueWriterTests.

  JSON-side scalar kinds and writers are unchanged."
  ```

---

## Acceptance (after both commits)

- [ ] `dotnet build Structura.slnx` clean — zero warnings, zero errors.
- [ ] `dotnet test` green — both `Structura.UnitTests` and `Structura.IntegrationTests`.
- [ ] `dotnet run --project src/Structura/Structura.csproj` produces the same byte-equal untouched regions of both XML samples as step 13 (the patch engine doesn't change), and the diff reporters show every mutated span exactly as today.
- [ ] `git log --oneline -3` shows two new commits on top — the feat and the refactor — in that order.
