using Structura.Generated;
using Structura.Runtime;
using Structura.Reporting;

string orderJson = File.ReadAllText("Samples/order.sample.json");
var order = orderJson.ParseJson<OrderSampleJson>();

order.Currency = "USD";

var modifiedJson = order.ToJson();

SimpleReporter.Print(order);
ConsoleDiffReporter.Print(order);
