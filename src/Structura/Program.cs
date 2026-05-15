using Structura.Common;
using Structura.Generated;
using Structura.Reporting;
using Structura.Runtime;

// ── JSON pipeline ─────────────────────────────────────────────────────────────

string orderFile = ProjectFolders.Samples.AppendPath("order.sample.json");
string orderJson = orderFile.ReadAllText();

var order = orderJson.ParseJson<OrderSampleJson>();

order.Currency = "USD";
order.Version = 42;
order.IsPriority = false;

DiffReporter.Print(order);

// ── XML pipeline ──────────────────────────────────────────────────────────────

string xmlFile = ProjectFolders.Samples.AppendPath("blrwbl.sample.xml");
string xmlContent = xmlFile.ReadAllText();

var document = xmlContent.ParseXml<BlrwblSampleXml>();

document.Document.DocumentID = "X-001";
document.Shipper.GLN = "9999988880001";
document.Total.TotalAmount = "700.00";

DiffReporter.Print(document);

string modifiedDocument = document.ToXml();
string outputFile = ProjectFolders.Samples.AppendPath("output.xml");
outputFile.WriteAllText(modifiedDocument);
