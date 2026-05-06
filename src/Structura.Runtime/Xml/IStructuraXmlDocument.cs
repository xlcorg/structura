namespace Structura.Runtime.Xml;

/// <summary>
/// Implemented by every generated model that is parsed from XML. The
/// <see cref="ParseFromXml"/> static factory is invoked by the
/// <c>ParseXml&lt;T&gt;</c> extension method.
/// </summary>
public interface IStructuraXmlDocument<TSelf> where TSelf : IStructuraXmlDocument<TSelf>
{
    static abstract TSelf ParseFromXml(string source);
}
