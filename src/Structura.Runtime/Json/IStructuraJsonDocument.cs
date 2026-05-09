namespace Structura.Runtime.Json;

/// <summary>
/// Implemented by every generated model that is parsed from JSON. The
/// <see cref="ParseFromJson"/> static factory is invoked by the
/// <c>ParseJson&lt;T&gt;</c> extension method. <see cref="SourceFileName"/>
/// is the original sample file name (e.g. <c>"order.sample.json"</c>) baked
/// into the generated model at codegen time and used as
/// <see cref="IStructuraDocument.DocumentName"/>.
/// </summary>
public interface IStructuraJsonDocument<TSelf> where TSelf : IStructuraJsonDocument<TSelf>
{
    static abstract string SourceFileName { get; }

    static abstract TSelf ParseFromJson(string source);
}
