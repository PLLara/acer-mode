using System.Text.Json;
using System.Text.Json.Serialization;

namespace AcerMode.Core;

// Enum gravado por nome; um nome desconhecido (de uma versão futura) vira o valor padrão
// em vez de tornar o arquivo inteiro inválido. Normalize() corrige o padrão depois.
public sealed class TolerantEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type t) => t.IsEnum;

    public override JsonConverter CreateConverter(Type t, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(TolerantEnumConverter<>).MakeGenericType(t))!;
}

public sealed class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    // Chave de dicionário desconhecida vira um valor fora do enum, que Normalize() descarta.
    static readonly T Unknown = (T)Enum.ToObject(typeof(T), 255);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<T>(reader.GetString(), true, out var v) && Enum.IsDefined(v)) return v;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var n) && Enum.IsDefined(typeof(T), Enum.ToObject(typeof(T), n))) return (T)Enum.ToObject(typeof(T), n);
        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());

    public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Enum.TryParse<T>(reader.GetString(), true, out var v) && Enum.IsDefined(v) ? v : Unknown;

    public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WritePropertyName(value.ToString());
}
