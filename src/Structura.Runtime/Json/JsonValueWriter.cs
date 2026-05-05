using System.Globalization;
using System.Text;

namespace Structura.Runtime.Json;

/// <summary>
/// Writes scalar values as JSON literals. Used by generated models when a
/// property is mutated — the resulting literal becomes the replacement text
/// in a <see cref="TextEdit"/>. Untouched values are never re-emitted.
/// </summary>
public static class JsonValueWriter
{
    public const string NullLiteral = "null";

    public static string WriteString(string? value)
    {
        if (value is null)
        {
            return NullLiteral;
        }

        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    public static string WriteInt64(long value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static string WriteInt32(int value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static string WriteDouble(double value)
        => value.ToString("R", CultureInfo.InvariantCulture);

    public static string WriteDecimal(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static string WriteBoolean(bool value)
        => value ? "true" : "false";
}
