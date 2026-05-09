// Polyfill so the netstandard2.0 source generator can use C# 9+ `init` accessors.
// The compiler synthesizes a reference to System.Runtime.CompilerServices.IsExternalInit
// for every `init` setter; netstandard2.0 doesn't ship the type, so we declare it here.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
