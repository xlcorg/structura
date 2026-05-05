namespace Structura.Runtime.Json;

/// <summary>
/// Implemented by every generated model that is parsed from JSON. The
/// <see cref="ParseFromJson"/> static factory is invoked by the
/// <c>ParseJson&lt;T&gt;</c> extension method.
/// </summary>
public interface IStructuraJsonDocument<TSelf> where TSelf : IStructuraJsonDocument<TSelf>
{
    static abstract TSelf ParseFromJson(string source);
}
