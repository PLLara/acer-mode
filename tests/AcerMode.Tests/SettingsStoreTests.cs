using AcerMode.Core;
using Xunit;

public class SettingsStoreTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "acermode-tests-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(dir, "settings.json");
    public SettingsStoreTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void Defaults_MatchLegacyColors()
    {
        var s = AppSettings.Defaults();
        Assert.Equal("#FF2D0A", s.Profile(ThermalMode.Turbo).Lighting.Color.ToHex());
        Assert.Equal(45, s.Profile(ThermalMode.Quiet).Lighting.Brightness);
        Assert.Equal(FanMode.Auto, s.Profile(ThermalMode.Eco).Fans.Mode);
        Assert.Equal(5, s.Profiles.Count);
    }

    [Fact]
    public void MissingFile_GivesDefaults()
    {
        var (s, warning) = new SettingsStore(FilePath).Load();
        Assert.Null(warning);
        Assert.Equal(5, s.Profiles.Count);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new SettingsStore(FilePath);
        var s = AppSettings.Defaults();
        s.Profile(ThermalMode.Turbo).Lighting = new LightingProfile { Effect = KbEffect.Wave, Speed = 8, Color = new Rgb(1, 2, 3) };
        s.Profile(ThermalMode.Turbo).Fans = new FanSettings(FanMode.Custom, 70, 55);
        store.Save(s);
        var (loaded, _) = store.Load();
        Assert.Equal(KbEffect.Wave, loaded.Profile(ThermalMode.Turbo).Lighting.Effect);
        Assert.Equal(new FanSettings(FanMode.Custom, 70, 55), loaded.Profile(ThermalMode.Turbo).Fans);
        Assert.Contains("\"#010203\"", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Corrupt_IsBackedUpAndReplaced()
    {
        File.WriteAllText(FilePath, "{ isto não é json");
        var (s, warning) = new SettingsStore(FilePath).Load();
        Assert.NotNull(warning);
        Assert.Equal(5, s.Profiles.Count);
        Assert.Single(Directory.GetFiles(dir, "settings.bad-*.json"));
    }

    [Fact]
    public void NewerVersion_PartialProfiles_UnknownFieldsPreserved()
    {
        File.WriteAllText(FilePath, """
        { "version": 2, "futureThing": { "a": 1 }, "showOsd": false,
          "profiles": { "Turbo": { "lighting": { "effect": "Neon", "brightness": 250 } } } }
        """);
        var store = new SettingsStore(FilePath);
        var (s, warning) = store.Load();
        Assert.Null(warning);
        Assert.False(s.ShowOsd);
        Assert.Equal(KbEffect.Neon, s.Profile(ThermalMode.Turbo).Lighting.Effect);
        Assert.Equal(100, s.Profile(ThermalMode.Turbo).Lighting.Brightness);
        Assert.Equal(5, s.Profiles.Count);
        store.Save(s);
        Assert.Contains("futureThing", File.ReadAllText(FilePath));
    }

    [Fact]
    public void NullProfile_IsReplacedByDefault()
    {
        File.WriteAllText(FilePath, """{ "profiles": { "Turbo": null } }""");
        var (s, warning) = new SettingsStore(FilePath).Load();
        Assert.Null(warning);
        Assert.Equal("#FF2D0A", s.Profile(ThermalMode.Turbo).Lighting.Color.ToHex());
    }

    [Fact]
    public void UnreadableFile_GivesDefaultsWithWarning()
    {
        File.WriteAllText(FilePath, "{}");
        using var lockIt = new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var (s, warning) = new SettingsStore(FilePath).Load();
        Assert.NotNull(warning);
        Assert.Equal(5, s.Profiles.Count);
    }

    [Fact]
    public void UnknownEnumValuesAndKeys_LoadWithDefaults()
    {
        File.WriteAllText(FilePath, """
        { "profiles": {
            "Overclock": { "fans": { "mode": "Auto" } },
            "Turbo": { "lighting": { "effect": "Rainbow", "direction": "Diagonal", "zones": ["#FF0000", null, "#00FF00", "#0000FF"] },
                       "fans": { "mode": "Curve", "cpuPercent": 70 } } } }
        """);
        var (s, warning) = new SettingsStore(FilePath).Load();
        Assert.Null(warning);
        var turbo = s.Profile(ThermalMode.Turbo);
        Assert.Equal(KbEffect.Static, turbo.Lighting.Effect);
        Assert.Equal(KbDirection.RightToLeft, turbo.Lighting.Direction);
        Assert.Equal(new Rgb(255, 0, 0), turbo.Lighting.Zones[0]);
        Assert.Equal(FanMode.Auto, turbo.Fans.Mode);
        Assert.Equal(70, turbo.Fans.CpuPercent);
        Assert.Equal(5, s.Profiles.Count);
    }

    [Fact]
    public void UnknownFieldsInsideProfile_ArePreserved()
    {
        File.WriteAllText(FilePath, """{ "profiles": { "Eco": { "futureProfileThing": 7 } } }""");
        var store = new SettingsStore(FilePath);
        var (s, _) = store.Load();
        store.Save(s);
        Assert.Contains("futureProfileThing", File.ReadAllText(FilePath));
    }

    [Fact]
    public void IndependentLighting_StartsAsCopyOfActive()
    {
        var s = AppSettings.Defaults();
        s.SetIndependentLighting(true, ThermalMode.Eco);
        Assert.Equal(s.Profile(ThermalMode.Eco).Lighting.Color, s.LightingFor(ThermalMode.Turbo).Color);
        s.SetIndependentLighting(false, ThermalMode.Eco);
        Assert.NotNull(s.GlobalLighting);
        Assert.Equal("#FF2D0A", s.LightingFor(ThermalMode.Turbo).Color.ToHex());
    }
}
