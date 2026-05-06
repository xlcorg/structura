// Demo host. End-to-end JSON + XML pipelines: ParseJson<T> / ParseXml<T> ->
// mutate -> ToJson / ToXml + SimpleReporter / ConsoleDiffReporter. See
// CLAUDE.md for the target API and Samples/*.{json,xml} for the documents.

using Structura.Common;
using Structura.Generated;
using Structura.Reporting;
using Structura.Runtime;

// ── JSON pipeline ─────────────────────────────────────────────────────────────

string orderJsonPath = Path.Combine(AppContext.BaseDirectory, "Samples", "order.sample.json");
string orderJson = orderJsonPath.ReadAllText();

var order = orderJson.ParseJson<OrderSampleJson>();

order.Currency = "USD";
order.Version = 42;
order.IsPriority = false;

string modifiedJson = order.ToJson();

Console.WriteLine("=== Modified JSON ===");
Console.WriteLine(modifiedJson);
Console.WriteLine();

Console.WriteLine("=== Changes (SimpleReporter) ===");
SimpleReporter.Print(order);
Console.WriteLine();

Console.WriteLine("=== Diff (ConsoleDiffReporter) ===");
ConsoleDiffReporter.Print(order);
Console.WriteLine();

// ── XML pipeline ──────────────────────────────────────────────────────────────

string blrwblPath = Path.Combine(AppContext.BaseDirectory, "Samples", "blrwbl.sample.xml");
string blrwblXml = blrwblPath.ReadAllText();

var waybill = blrwblXml.ParseXml<BlrwblSampleXml>();

string roundTripped = waybill.ToXml();
bool identical = string.Equals(roundTripped, blrwblXml, StringComparison.Ordinal);

Console.WriteLine("=== BLRWBL XML round-trip ===");
Console.WriteLine($"Parsed without exception. ToXml() identical to source: {identical}");
Console.WriteLine();

Console.WriteLine("=== BLRWBL Changes (no mutations applied) ===");
SimpleReporter.Print(waybill);
