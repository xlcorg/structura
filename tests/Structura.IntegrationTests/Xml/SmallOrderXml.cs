using System.Globalization;

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
    private long _version;
    private bool _isPriority;

    private SmallOrderXml(string source, XmlSourceElement root)
    {
        _ctx = new StructuraDocumentContext(source, "small-order.xml");

        XmlSourceElement currency = root.RequireElement("currency");
        _currencyValueSpan = currency.InnerSpan;
        _currency = ((XmlSourceText)currency.Children[0]).Value;

        XmlSourceElement version = root.RequireElement("version");
        _versionValueSpan = version.InnerSpan;
        _version = long.Parse(
            ((XmlSourceText)version.Children[0]).Value,
            CultureInfo.InvariantCulture);

        XmlSourceElement isPriority = root.RequireElement("is_priority");
        _isPriorityValueSpan = isPriority.InnerSpan;
        _isPriority = bool.Parse(((XmlSourceText)isPriority.Children[0]).Value);
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

    public long Version
    {
        get => _version;
        set
        {
            _version = value;
            _ctx.Record("/version", _versionValueSpan, XmlValueWriter.WriteInt64(value));
        }
    }

    public bool IsPriority
    {
        get => _isPriority;
        set
        {
            _isPriority = value;
            _ctx.Record("/is_priority", _isPriorityValueSpan, XmlValueWriter.WriteBoolean(value));
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
