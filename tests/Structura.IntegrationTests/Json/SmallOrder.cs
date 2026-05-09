using System.Globalization;

using Structura.Runtime;
using Structura.Runtime.Json;

namespace Structura.IntegrationTests.Json;

/// <summary>
/// Hand-written stand-in for a generator-produced model. Demonstrates the
/// exact pattern the source generator must emit on step 3:
///   - private <see cref="StructuraDocumentContext"/> field
///   - private cached value + original-span field per scalar property
///   - setter calls <c>_ctx.Record(path, span, JsonValueWriter.WriteX(value))</c>
///   - explicit <see cref="IStructuraDocument"/> implementation forwards to context
/// Intentionally simple — no nested objects, arrays, or nullables here. Those
/// shapes belong to the generator design (step 3).
/// </summary>
public sealed class SmallOrder : IStructuraJsonDocument<SmallOrder>, IStructuraDocument
{
    private readonly StructuraDocumentContext _ctx;

    private readonly TextSpan _currencyValueSpan;
    private readonly TextSpan _versionValueSpan;
    private readonly TextSpan _isPriorityValueSpan;

    private string _currency;
    private long _version;
    private bool _isPriority;

    private SmallOrder(string source, JsonSourceObject root)
    {
        _ctx = new StructuraDocumentContext(source, "small-order.json");

        var currency = (JsonSourceString)root.RequireProperty("currency").Value;
        _currencyValueSpan = currency.Span;
        _currency = currency.Value;

        var version = (JsonSourceNumber)root.RequireProperty("version").Value;
        _versionValueSpan = version.Span;
        _version = long.Parse(version.Literal, CultureInfo.InvariantCulture);

        var isPriority = (JsonSourceBoolean)root.RequireProperty("is_priority").Value;
        _isPriorityValueSpan = isPriority.Span;
        _isPriority = isPriority.Value;
    }

    public static string SourceFileName => "small-order.json";

    public static SmallOrder ParseFromJson(string source)
    {
        JsonSourceNode root = JsonSourceParser.Parse(source);
        if (root is not JsonSourceObject obj)
        {
            throw new JsonParseException("Expected JSON object at root.");
        }
        return new SmallOrder(source, obj);
    }

    public string Currency
    {
        get => _currency;
        set
        {
            _currency = value;
            _ctx.Record("/currency", _currencyValueSpan, JsonValueWriter.WriteString(value));
        }
    }

    public long Version
    {
        get => _version;
        set
        {
            _version = value;
            _ctx.Record("/version", _versionValueSpan, JsonValueWriter.WriteInt64(value));
        }
    }

    public bool IsPriority
    {
        get => _isPriority;
        set
        {
            _isPriority = value;
            _ctx.Record("/is_priority", _isPriorityValueSpan, JsonValueWriter.WriteBoolean(value));
        }
    }

    public string ToJson()
    {
        return _ctx.ApplyEdits();
    }

    string IStructuraDocument.OriginalText => _ctx.OriginalText;
    string IStructuraDocument.CurrentText => _ctx.ApplyEdits();
    string IStructuraDocument.DocumentName => _ctx.DocumentName;
    IReadOnlyList<DocumentChange> IStructuraDocument.Changes => _ctx.Changes;
}
