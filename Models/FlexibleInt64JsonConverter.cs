using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace unity_cli_ui.Models;

public sealed class FlexibleInt64JsonConverter : JsonConverter<long>
{
    public override long Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => ReadNumber(ref reader),
            JsonTokenType.String => ReadString(ref reader),
            JsonTokenType.Null => 0,
            _ => throw new JsonException($"Cannot convert {reader.TokenType} to Int64.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        long value,
        JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

    private static long ReadNumber(ref Utf8JsonReader reader)
    {
        if (reader.TryGetInt64(out var value))
        {
            return value;
        }
        if (reader.TryGetDecimal(out var decimalValue))
        {
            return ToInt64(decimalValue);
        }
        if (reader.TryGetDouble(out var doubleValue) &&
            double.IsFinite(doubleValue) &&
            doubleValue >= long.MinValue &&
            doubleValue <= long.MaxValue)
        {
            return checked((long)Math.Truncate(doubleValue));
        }

        throw new JsonException("Cannot convert number to Int64.");
    }

    private static long ReadString(ref Utf8JsonReader reader)
    {
        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return ToInt64(decimalValue);
        }

        throw new JsonException("Cannot convert string to Int64.");
    }

    private static long ToInt64(decimal value)
    {
        if (value < long.MinValue || value > long.MaxValue)
        {
            throw new JsonException("Number is outside the Int64 range.");
        }

        return decimal.ToInt64(decimal.Truncate(value));
    }
}
