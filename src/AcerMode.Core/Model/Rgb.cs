using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AcerMode.Core;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(string hex) =>
        TryFromHex(hex, out var c) ? c : throw new FormatException("Cor inválida: " + hex);

    public static bool TryFromHex(string? hex, out Rgb color)
    {
        color = default;
        var s = hex?.Trim().TrimStart('#');
        if (s is null || s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return false;
        color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}

// Cores ficam legíveis no settings.json ("#FF2D0A").
public sealed class RgbJsonConverter : JsonConverter<Rgb>
{
    public override Rgb Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Rgb.TryFromHex(reader.GetString(), out var c) ? c : throw new JsonException("Cor inválida");

    public override void Write(Utf8JsonWriter writer, Rgb value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToHex());
}
