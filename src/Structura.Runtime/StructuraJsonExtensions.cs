using Structura.Runtime.Json;

namespace Structura.Runtime;

/// <summary>
/// User-facing entry points for parsing JSON into a generated Structura model.
/// </summary>
public static class StructuraJsonExtensions
{
    /// <summary>
    /// Parses <paramref name="json"/> into <typeparamref name="T"/>. The model
    /// retains a reference to the original text and emits minimal text edits
    /// against it when its properties are mutated.
    /// </summary>
    public static T ParseJson<T>(this string json) where T : IStructuraJsonDocument<T>
        => T.ParseFromJson(json);
}
