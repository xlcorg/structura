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

order.Customer.FirstName = "Ivan";
order.Customer.Preferences.MarketingConsent = false;
order.BillingAddress.City = "Rotterdam";
order.Items[0].Quantity = 2;
order.Items[1].Manufacturer.CountryCode = "DE";

foreach (var lineItem in order.Items)
{
    Console.WriteLine($"{lineItem.LineId}: {lineItem.Price.CurrencyCode} {lineItem.Price.PriceWithVat.Value}");
}

string modifiedJson = order.ToJson();

Console.WriteLine("=== Modified JSON ===");
Console.WriteLine(modifiedJson);
Console.WriteLine();

Console.WriteLine("=== Diff (UnifiedDiffReporter) ===");
UnifiedDiffReporter.Print(order);
Console.WriteLine();

Console.WriteLine("=== Diff (SideBySideDiffReporter) ===");
SideBySideDiffReporter.Print(order);
Console.WriteLine();

// ── XML pipeline ──────────────────────────────────────────────────────────────

string blrwblPath = Path.Combine(AppContext.BaseDirectory, "Samples", "blrwbl.sample.xml");
string blrwblXml = blrwblPath.ReadAllText();

var waybill = blrwblXml.ParseXml<BlrwblSampleXml>();

waybill.Currency = "USD";
waybill.SealID = "99999";
waybill.Document.DocumentID = "X-001";
waybill.Shipper.GLN = "9999988880001";
waybill.Total.TotalAmount = "700.00";

foreach (BlrwblSampleXml.LineItem lineItem in waybill.DespatchAdviceLogisticUnitLineItem.LineItems)
{
    if (lineItem.LineItemNumber == "2")
    {
        lineItem.LineItemNumber = "42";
    }

    Console.WriteLine($"{lineItem.LineItemNumber}. {lineItem.LineItemName}");
}

string modifiedBlrwbl = waybill.ToXml();

Console.WriteLine("=== Modified BLRWBL XML ===");
Console.WriteLine(modifiedBlrwbl);
Console.WriteLine();

Console.WriteLine("=== BLRWBL Diff (UnifiedDiffReporter) ===");
UnifiedDiffReporter.Print(waybill);
Console.WriteLine();

Console.WriteLine("=== BLRWBL Diff (SideBySideDiffReporter) ===");
SideBySideDiffReporter.Print(waybill);
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

Console.WriteLine("=== Library Diff (UnifiedDiffReporter) ===");
UnifiedDiffReporter.Print(library);
Console.WriteLine();

Console.WriteLine("=== Library Diff (SideBySideDiffReporter) ===");
SideBySideDiffReporter.Print(library);
Console.WriteLine();

// ── Library JSON pipeline (heterogeneous-item torture sample, JSON side) ─────

string libraryJsonPath = Path.Combine(AppContext.BaseDirectory, "Samples", "library.sample.json");
string libraryJson = libraryJsonPath.ReadAllText();

var libraryDoc = libraryJson.ParseJson<LibrarySampleJson>();

libraryDoc.Name = "City Library";
libraryDoc.Books[1].Publisher.Address.City = "Saint Petersburg";
libraryDoc.Books[2].Publisher.Address.City = "Lyon";

Console.WriteLine("=== Library JSON tags / years ===");
Console.WriteLine($"tags:  {string.Join(", ", libraryDoc.Tags)}");
Console.WriteLine($"years: {string.Join(", ", libraryDoc.Years)}");
Console.WriteLine();

try
{
    libraryDoc.Books[1].Subtitle = "anything";
}
catch (StructuraMutationException ex)
{
    Console.WriteLine($"=== Throwing setter (expected) ===");
    Console.WriteLine(ex.Message);
    Console.WriteLine();
}

string modifiedLibraryJson = libraryDoc.ToJson();

Console.WriteLine("=== Modified Library JSON ===");
Console.WriteLine(modifiedLibraryJson);
Console.WriteLine();

Console.WriteLine("=== Library JSON Diff (UnifiedDiffReporter) ===");
UnifiedDiffReporter.Print(libraryDoc);
Console.WriteLine();

Console.WriteLine("=== Library JSON Diff (SideBySideDiffReporter) ===");
SideBySideDiffReporter.Print(libraryDoc);
Console.WriteLine();
