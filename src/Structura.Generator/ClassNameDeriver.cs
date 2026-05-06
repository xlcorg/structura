using System;
using System.Text;

namespace Structura.Generator;

/// <summary>
/// Derives a C# class name from a sample-file name.
/// <c>order.sample.json</c> → <c>OrderSampleJson</c>.
/// </summary>
internal static class ClassNameDeriver
{
    /// <summary>
    /// Takes a bare file name (no directory path) such as
    /// <c>order.sample.json</c> and returns the PascalCase class name.
    /// </summary>
    public static string Derive(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return "UnknownDocument";
        }

        string[] parts = fileName.Split('.');
        var sb = new StringBuilder(fileName.Length);

        foreach (string part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                continue;
            }

            if (char.IsLetter(part[0]))
            {
                sb.Append(char.ToUpperInvariant(part[0]));
                sb.Append(part, 1, part.Length - 1);
            }
            else
            {
                sb.Append(part);
            }
        }

        var result = sb.ToString();
        return string.IsNullOrEmpty(result) ? "UnknownDocument" : result;
    }
}
