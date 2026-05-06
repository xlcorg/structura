// Demo host. End-to-end JSON pipeline: ParseJson<T> -> mutate -> ToJson +
// SimpleReporter / ConsoleDiffReporter. See CLAUDE.md for the target API and
// Samples/order.sample.json for the document it operates on.

using Structura.Generated;
using Structura.Reporting;
using Structura.Runtime;

string samplePath = Path.Combine(AppContext.BaseDirectory, "Samples", "order.sample.json");
string orderJson = File.ReadAllText(samplePath);

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
