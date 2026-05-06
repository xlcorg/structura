using System;
using System.Collections.Generic;
using System.Text;

namespace Structura.Generator;

/// <summary>
/// Converts a JSON object key into a valid C# PascalCase identifier and
/// resolves collisions against a set of already-used names.
/// </summary>
internal static class IdentifierSanitizer
{
    /// <summary>
    /// Converts <paramref name="jsonKey"/> to a PascalCase C# identifier that
    /// does not appear in <paramref name="usedNames"/>. The chosen name is added
    /// to <paramref name="usedNames"/> before returning.
    /// </summary>
    public static string Sanitize(string jsonKey, HashSet<string> usedNames)
    {
        var candidate = ToPascalCase(jsonKey);
        if (string.IsNullOrEmpty(candidate))
        {
            candidate = "_Field";
        }

        var result = candidate;
        var suffix = 2;
        while (!usedNames.Add(result))
        {
            result = candidate + suffix.ToString();
            suffix++;
        }

        return result;
    }

    /// <summary>
    /// Converts <paramref name="jsonKey"/> to a PascalCase identifier.
    /// Separators: <c>-</c>, <c>_</c>, whitespace, <c>.</c>.
    /// If the resulting identifier starts with a digit it is prefixed with <c>_</c>.
    /// </summary>
    internal static string ToPascalCase(string jsonKey)
    {
        if (string.IsNullOrEmpty(jsonKey))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(jsonKey.Length);
        var newToken = true;

        foreach (var c in jsonKey)
        {
            if (c == '-' || c == '_' || c == ' ' || c == '\t' || c == '.')
            {
                newToken = true;
                continue;
            }

            if (newToken)
            {
                sb.Append(char.IsLetter(c) ? char.ToUpperInvariant(c) : c);
                newToken = false;
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0 && char.IsDigit(sb[0]))
        {
            sb.Insert(0, '_');
        }

        return sb.ToString();
    }
}
