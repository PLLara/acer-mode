using System.Text.Json;
using System.Text.Json.Serialization;

namespace AcerMode.Core;

public sealed class SettingsStore
{
    public static string DefaultDirectory =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AcerMode");

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new TolerantEnumConverterFactory(), new RgbJsonConverter() },
    };

    readonly string path;
    public SettingsStore(string path) => this.path = path;

    public (AppSettings Settings, string? Warning) Load()
    {
        if (!File.Exists(path)) return (AppSettings.Defaults(), null);
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? throw new JsonException("Arquivo vazio.");
            s.Normalize();
            return (s, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Arquivo preso por antivírus/backup no logon: usa os padrões sem mexer no arquivo.
            return (AppSettings.Defaults(), "Não foi possível ler as configurações (" + ex.Message + "). Usando os padrões.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or FormatException or InvalidOperationException or ArgumentException)
        {
            var bad = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, $"settings.bad-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try { File.Move(path, bad, overwrite: true); }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException) { }
            return (AppSettings.Defaults(), "As configurações estavam corrompidas e foram restauradas. Cópia salva em " + System.IO.Path.GetFileName(bad) + ".");
        }
    }

    // Grava num temporário e troca, para um desligamento no meio não corromper o arquivo.
    public void Save(AppSettings s)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
