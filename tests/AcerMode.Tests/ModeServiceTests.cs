using AcerMode.Core;
using Xunit;

public class ModeServiceTests
{
    readonly FakeHardware hw = new();
    readonly AppSettings settings = AppSettings.Defaults();
    readonly DirtyMarker dirty = new(Path.Combine(Path.GetTempPath(), "acermode-ms-" + Guid.NewGuid().ToString("N")));
    ModeService Svc() => new(hw, () => settings, dirty);

    [Fact]
    public async Task Apply_SetsModeThenLightingThenFans()
    {
        var r = await Svc().ApplyModeAsync(ThermalMode.Turbo);
        Assert.Empty(r.Warnings);
        Assert.Equal(new[] { "mode:Turbo", "lighting:Static", "fans:Auto" }, hw.Calls);
        Assert.Equal(new Rgb(255, 45, 10), hw.Lighting!.Zones[0]);
    }

    [Fact]
    public async Task ModeFailure_AppliesNothingElse()
    {
        hw.FailOn = op => op == "mode";
        await Assert.ThrowsAsync<HardwareException>(() => Svc().ApplyModeAsync(ThermalMode.Turbo));
        Assert.Equal(new[] { "mode:Turbo" }, hw.Calls);
    }

    [Fact]
    public async Task LightingFailure_KeepsModeAndWarns()
    {
        hw.FailOn = op => op == "lighting";
        var svc = Svc();
        var r = await svc.ApplyModeAsync(ThermalMode.Eco);
        Assert.Equal(ThermalMode.Eco, hw.Mode);
        Assert.Equal(ThermalMode.Eco, svc.Active);
        Assert.Contains(r.Warnings, w => w.StartsWith("Iluminação"));
    }

    [Fact]
    public async Task CustomFans_SetDirtyMarker_AutoClearsIt()
    {
        settings.Profile(ThermalMode.Turbo).Fans = new FanSettings(FanMode.Custom, 50, 50);
        var svc = Svc();
        await svc.ApplyModeAsync(ThermalMode.Turbo);
        Assert.True(dirty.Exists);
        await svc.ApplyModeAsync(ThermalMode.Balanced);
        Assert.False(dirty.Exists);
    }

    [Fact]
    public async Task FanFailure_FallsBackToAuto()
    {
        settings.Profile(ThermalMode.Turbo).Fans = new FanSettings(FanMode.Custom, 50, 50);
        int fanCalls = 0;
        hw.FailOn = op => op == "fans" && fanCalls++ == 0;
        var svc = Svc();
        var r = await svc.ApplyModeAsync(ThermalMode.Turbo);
        Assert.Contains(r.Warnings, w => w.StartsWith("Ventoinhas"));
        Assert.Equal(FanMode.Auto, hw.Fans.Mode);
        Assert.Equal(FanMode.Auto, svc.ActiveFans.Mode);
        Assert.False(dirty.Exists);
    }

    [Fact]
    public async Task CustomWriteAndRevertBothFail_KeepsGuardArmed()
    {
        // A escrita pode ter chegado à BIOS mesmo falhando aqui: trata como Personalizado.
        settings.Profile(ThermalMode.Turbo).Fans = new FanSettings(FanMode.Custom, 50, 50);
        hw.FailOn = op => op == "fans";
        var svc = Svc();
        await svc.ApplyModeAsync(ThermalMode.Turbo);
        Assert.Equal(FanMode.Custom, svc.ActiveFans.Mode);
        Assert.True(dirty.Exists);
    }

    [Fact]
    public async Task ApplyWithoutFans_LeavesFansUntouched()
    {
        settings.Profile(ThermalMode.Turbo).Fans = new FanSettings(FanMode.Custom, 50, 50);
        await Svc().ApplyModeAsync(ThermalMode.Turbo, applyFans: false);
        Assert.DoesNotContain(hw.Calls, c => c.StartsWith("fans"));
        Assert.False(dirty.Exists);
    }

    [Fact]
    public async Task PreviewForAnotherMode_IsDroppedOnceTargetChanged()
    {
        var svc = Svc();
        await svc.ApplyModeAsync(ThermalMode.Balanced);
        hw.Delay = TimeSpan.FromMilliseconds(40);
        var toTurbo = svc.ApplyModeAsync(ThermalMode.Turbo);              // botão físico no meio da edição
        bool sent = await svc.PreviewLightingAsync(LightingProfile.Solid(new Rgb(1, 2, 3), 100), ThermalMode.Balanced);
        await toTurbo;
        Assert.False(sent);
        Assert.Equal(new Rgb(255, 45, 10), hw.Lighting!.Zones[0]);
    }

    [Fact]
    public async Task PreviewForTargetMode_IsSent()
    {
        var svc = Svc();
        await svc.ApplyModeAsync(ThermalMode.Eco);
        Assert.True(await svc.PreviewLightingAsync(LightingProfile.Solid(new Rgb(1, 2, 3), 100), ThermalMode.Eco));
        Assert.True(await svc.PreviewLightingAsync(LightingProfile.Solid(new Rgb(4, 5, 6), 100), null));   // iluminação independente
        Assert.Equal(new Rgb(4, 5, 6), hw.Lighting!.Zones[0]);
    }

    [Fact]
    public async Task SmartProfile_StartsOnBiosAuto()
    {
        settings.Profile(ThermalMode.Turbo).Fans = new FanSettings(FanMode.Smart);
        var svc = Svc();
        await svc.ApplyModeAsync(ThermalMode.Turbo);
        Assert.Equal(FanMode.Auto, hw.Fans.Mode);
        Assert.Equal(FanMode.Smart, svc.ActiveFans.Mode);
        Assert.False(dirty.Exists);
    }

    [Fact]
    public async Task SmartHotReading_BoostsFans_CoolReading_HandsBackToBios()
    {
        var now = new DateTime(2026, 10, 17, 12, 0, 0);
        settings.Profile(ThermalMode.Turbo).Fans = new FanSettings(FanMode.Smart);
        var svc = new ModeService(hw, () => settings, dirty, clock: () => now);
        await svc.ApplyModeAsync(ThermalMode.Turbo);

        await svc.OnReadingAsync(new SensorReading(95, 74, null, null, null, DateTime.Now));
        Assert.Equal(new FanSettings(FanMode.Custom, 100, 100), hw.Fans);
        Assert.True(dirty.Exists);
        Assert.Equal(FanMode.Smart, svc.ActiveFans.Mode);

        now = now.AddSeconds(5);
        await svc.OnReadingAsync(new SensorReading(60, 55, null, null, null, DateTime.Now));
        Assert.Equal(FanMode.Custom, hw.Fans.Mode);   // segura no mínimo 30 s antes de devolver

        now = now.AddSeconds(30);
        await svc.OnReadingAsync(new SensorReading(60, 55, null, null, null, DateTime.Now));
        Assert.Equal(FanMode.Auto, hw.Fans.Mode);
        Assert.False(dirty.Exists);
    }

    [Fact]
    public async Task SmartSameOutput_DoesNotRewrite()
    {
        settings.Profile(ThermalMode.Turbo).Fans = new FanSettings(FanMode.Smart);
        var svc = Svc();
        await svc.ApplyModeAsync(ThermalMode.Turbo);
        await svc.OnReadingAsync(new SensorReading(95, 74, null, null, null, DateTime.Now));
        int writes = hw.Calls.Count(c => c.StartsWith("fans"));
        await svc.OnReadingAsync(new SensorReading(96, 75, null, null, null, DateTime.Now));
        Assert.Equal(writes, hw.Calls.Count(c => c.StartsWith("fans")));
    }

    static SensorReading Temp(int c) => new(c, 50, null, null, null, DateTime.Now);

    [Fact]
    public async Task Smart_SpeedChangeWhileBoosted_WritesOnlySpeeds()
    {
        settings.Profile(ThermalMode.Turbo).Fans = new FanSettings(FanMode.Smart);
        var svc = Svc();
        await svc.ApplyModeAsync(ThermalMode.Turbo);
        await svc.OnReadingAsync(Temp(70));          // assume a 55%
        hw.Calls.Clear();
        await svc.OnReadingAsync(Temp(72));          // alvo 60%: menos de 10, não escreve
        Assert.Empty(hw.Calls);
        await svc.OnReadingAsync(Temp(80));          // alvo 80%
        Assert.Equal(new[] { "fanSpeed:80" }, hw.Calls);
        Assert.Equal(80, hw.Fans.CpuPercent);
    }

    [Fact]
    public async Task Smart_Drop_IsHeldFor20s()
    {
        var now = new DateTime(2026, 10, 17, 12, 0, 0);
        settings.Profile(ThermalMode.Turbo).Fans = new FanSettings(FanMode.Smart);
        var svc = new ModeService(hw, () => settings, dirty, clock: () => now);
        await svc.ApplyModeAsync(ThermalMode.Turbo);
        await svc.OnReadingAsync(Temp(95));          // 100%
        hw.Calls.Clear();
        for (int i = 0; i < 5; i++) { now = now.AddSeconds(2); await svc.OnReadingAsync(Temp(75)); }
        Assert.Empty(hw.Calls);                      // 10 s: ainda segura
        for (int i = 0; i < 6; i++) { now = now.AddSeconds(2); await svc.OnReadingAsync(Temp(75)); }
        Assert.Single(hw.Calls, c => c.StartsWith("fanSpeed:"));   // depois de 20 s, uma escrita
    }

    [Fact]
    public async Task Reading_IgnoredWhenNotSmart()
    {
        var svc = Svc();
        await svc.ApplyModeAsync(ThermalMode.Turbo);   // perfil padrão do teste: Auto
        settings.Profile(ThermalMode.Turbo).Fans = new FanSettings(FanMode.Auto);
        await svc.ApplyFansAsync(new FanSettings(FanMode.Auto));
        int writes = hw.Calls.Count;
        await svc.OnReadingAsync(new SensorReading(95, 74, null, null, null, DateTime.Now));
        Assert.Equal(writes, hw.Calls.Count);
    }

    [Fact]
    public async Task ToggleTurbo_GoesAndComesBack()
    {
        var svc = Svc();
        await svc.ApplyModeAsync(ThermalMode.Quiet);
        await svc.ToggleTurboAsync();
        Assert.Equal(ThermalMode.Turbo, hw.Mode);
        await svc.ToggleTurboAsync();
        Assert.Equal(ThermalMode.Quiet, hw.Mode);
        Assert.Equal(new Rgb(0, 190, 255), hw.Lighting!.Zones[0]);   // iluminação do modo novo, não da edição
    }

    [Fact]
    public async Task RecoverAfterCrash_RevertsWhenDirty()
    {
        dirty.Set();
        await Svc().RecoverAfterCrashAsync();
        Assert.Equal(new[] { "fans:Auto" }, hw.Calls);
        Assert.False(dirty.Exists);
    }

    [Fact]
    public async Task IndependentLighting_IgnoresModeProfile()
    {
        settings.SetIndependentLighting(true, ThermalMode.Eco);
        await Svc().ApplyModeAsync(ThermalMode.Turbo);
        Assert.Equal(new Rgb(40, 220, 90), hw.Lighting!.Zones[0]);
    }
}
