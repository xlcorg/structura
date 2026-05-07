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

waybill.Currency = "USD";
waybill.SealID = 99999;

foreach (BlrwblSampleXml.LineItem lineItem in waybill.DespatchAdviceLogisticUnitLineItem.LineItems)
{
    if (lineItem.LineItemNumber == 2)
    {
        lineItem.LineItemNumber = 42;
    }

    Console.WriteLine($"{lineItem.LineItemNumber}. {lineItem.LineItemName}");
}

string modifiedBlrwbl = waybill.ToXml();

Console.WriteLine("=== Modified BLRWBL XML ===");
Console.WriteLine(modifiedBlrwbl);
Console.WriteLine();

Console.WriteLine("=== BLRWBL Changes (SimpleReporter) ===");
SimpleReporter.Print(waybill);
Console.WriteLine();

Console.WriteLine("=== BLRWBL Diff (ConsoleDiffReporter) ===");
ConsoleDiffReporter.Print(waybill);
Console.WriteLine();

// ── Library pipeline (heterogeneous-item torture sample) ─────────────────────

string libraryPath = Path.Combine(AppContext.BaseDirectory, "Samples", "library.sample.xml");
string libraryXml = libraryPath.ReadAllText();

var library = libraryXml.ParseXml<LibrarySampleXml>();

library.Created = "2026-05-08";
library.Books[0].Title = "Мир и война";

string modifiedLibrary = library.ToXml();

Console.WriteLine("=== Modified Library XML ===");
Console.WriteLine(modifiedLibrary);
Console.WriteLine();

Console.WriteLine("=== Library Changes (SimpleReporter) ===");
SimpleReporter.Print(library);
Console.WriteLine();

Console.WriteLine("=== Library Diff (ConsoleDiffReporter) ===");
ConsoleDiffReporter.Print(library);
