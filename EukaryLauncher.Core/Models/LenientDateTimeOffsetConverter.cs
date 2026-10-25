using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EukaryLauncher.Core.Models;

/// <summary>
/// 宽容的 DateTimeOffset 解析。Fabric meta 返回的时间是 "+0000"（无冒号时区），
/// System.Text.Json 的默认解析不认这种格式——版本 JSON 的时间字段不是关键数据，解析不了就给 null。
/// </summary>
public sealed class LenientDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) return null;

        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var value))
            return value;

        return null;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteStringValue(value.Value.ToString("O", CultureInfo.InvariantCulture));
        else writer.WriteNullValue();
    }
}
