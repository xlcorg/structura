namespace Structura.Runtime.Xml;

/// <summary>
/// Implemented by every generated model that is parsed from XML. The
/// <see cref="ParseFromXml"/> static factory is invoked by the
/// <c>ParseXml&lt;T&gt;</c> extension method. <see cref="SourceFileName"/>
/// is the original sample file name baked into the generated model at
/// codegen time and used as <see cref="IStructuraDocument.DocumentName"/>.
/// </summary>
public interface IStructuraXmlDocument<TSelf> where TSelf : IStructuraXmlDocument<TSelf>
{
    static abstract string SourceFileName { get; }

    static abstract TSelf ParseFromXml(string source);
}
