using System.Text.Json;
using System.Text.Json.Serialization;

namespace EukaryLauncher.Core.Models;

/// <summary>
/// arguments 数组元素既可以是字符串，也可以是 { rules, value } 对象。
/// 官方格式里 value 还可能是字符串数组，这里统一摊平成 Values。
/// </summary>
public sealed class ArgumentEntryConverter : JsonConverter<ArgumentEntry>
{
    public override ArgumentEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return new ArgumentEntry { Values = { reader.GetString() ?? string.Empty } };

            case JsonTokenType.StartObject:
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                var root = doc.RootElement;
                var entry = new ArgumentEntry();

                if (root.TryGetProperty("rules", out var rules) &&
                    rules.ValueKind == JsonValueKind.Array)
                {
                    entry.Rules = rules.Deserialize<List<Rule>>(options);
                }

                if (root.TryGetProperty("value", out var value))
                {
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        entry.Values.Add(value.GetString() ?? string.Empty);
                    }
                    else if (value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                                entry.Values.Add(item.GetString() ?? string.Empty);
                            else
                                entry.Values.Add(item.ToString());
                        }
                    }
                }

                return entry;
            }

            default:
                throw new JsonException($"无法把 {reader.TokenType} 解析为启动参数项。");
        }
    }

    public override void Write(Utf8JsonWriter writer, ArgumentEntry value, JsonSerializerOptions options)
    {
        if (value.Rules is null && value.Values.Count == 1)
        {
            writer.WriteStringValue(value.Values[0]);
            return;
        }

        writer.WriteStartObject();
        if (value.Rules is not null)
        {
            writer.WritePropertyName("rules");
            JsonSerializer.Serialize(writer, value.Rules, options);
        }
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value.Values, options);
        writer.WriteEndObject();
    }
}
