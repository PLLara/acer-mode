using AcerMode.Core;
using Xunit;

public class SensorMonitorTests
{
    [Theory]
    [InlineData(1, true, FanMode.Auto, true)] [InlineData(2, true, FanMode.Auto, true)]
    [InlineData(1, false, FanMode.Auto, false)] [InlineData(3, false, FanMode.Max, false)]
    [InlineData(1, false, FanMode.Custom, false)] [InlineData(3, false, FanMode.Custom, true)] [InlineData(6, false, FanMode.Custom, true)]
    [InlineData(1, false, FanMode.Smart, false)] [InlineData(2, false, FanMode.Smart, true)] [InlineData(4, false, FanMode.Smart, true)]
    public void Cadence(long tick, bool visible, FanMode mode, bool expected) =>
        Assert.Equal(expected, SensorMonitor.ShouldPoll(tick, visible, mode));

    // Overclock ligado precisa da temperatura da GPU mesmo com ventoinha em Auto/Máximo.
    [Theory]
    [InlineData(1, false)] [InlineData(2, true)] [InlineData(4, true)]
    public void Cadence_OverclockActive_PollsEvery2s(long tick, bool expected) =>
        Assert.Equal(expected, SensorMonitor.ShouldPoll(tick, false, FanMode.Auto, overclockActive: true));

    [Fact]
    public async Task OverclockActive_ReadsFastTempsWithFansOnAuto()
    {
        var hw = new FakeHardware();
        var mon = new SensorMonitor(hw, () => new FanSettings(FanMode.Auto), new StubFast(70, 86)) { OverclockActive = () => true };
        await mon.TickAsync(2);
        Assert.Equal(86, mon.History.Latest!.GpuTempC);
        Assert.DoesNotContain(hw.Calls, c => c == "sensors");
    }

    [Fact]
    public async Task HotReading_TriggersGuard()
    {
        var hw = new FakeHardware { NextReading = new SensorReading(91, 60, null, null, null, DateTime.Now) };
        var mon = new SensorMonitor(hw, () => new FanSettings(FanMode.Custom, 40, 40)) { UiVisible = true };
        FanGuardAction? got = null;
        mon.GuardTriggered += (a, _) => got = a;
        await mon.TickAsync(1);
        Assert.Equal(FanGuardAction.SwitchToSmart, got);
        Assert.Equal(91, mon.History.Latest!.CpuTempC);
    }

    sealed class StubFast(int? cpu, int? gpu) : IFastTemps
    {
        public int? CpuC() => cpu;
        public int? GpuC() => gpu;
    }

    [Fact]
    public async Task Hidden_UsesFastTemps_NeverTheBios()
    {
        var hw = new FakeHardware();
        var mon = new SensorMonitor(hw, () => new FanSettings(FanMode.Smart), new StubFast(88, 72));
        for (long t = 1; t <= 20; t++) await mon.TickAsync(t);
        Assert.DoesNotContain(hw.Calls, c => c.StartsWith("sensors") || c.StartsWith("cpuTemp"));
        Assert.Equal(88, mon.History.Latest!.CpuTempC);
        Assert.Equal(72, mon.History.Latest!.GpuTempC);
    }

    sealed class SeqFast(params int[] cpu) : IFastTemps
    {
        int i;
        public int? CpuC() => cpu[i++ % cpu.Length];
        public int? GpuC() => 50;
    }

    [Fact]
    public async Task Hidden_SmoothsNoisyCoreTempSpikes()
    {
        // núcleo mais quente do Core Temp pula 60↔85 entre leituras; a curva não pode ver isso cru
        var mon = new SensorMonitor(new FakeHardware(), () => new FanSettings(FanMode.Smart), new SeqFast(60, 85));
        var seen = new List<int>();
        mon.Updated += r => seen.Add(r.CpuTempC!.Value);
        for (long t = 2; t <= 40; t += 2) await mon.TickAsync(t);
        var settled = seen.Skip(6).ToList();
        Assert.True(settled.Max() - settled.Min() <= 8, $"oscilou {settled.Min()}–{settled.Max()}");
        Assert.InRange(settled.Average(), 68, 78);
    }

    [Fact]
    public async Task Hidden_FirstReading_IsNotDampedFromZero()
    {
        var mon = new SensorMonitor(new FakeHardware(), () => new FanSettings(FanMode.Smart), new SeqFast(90));
        await mon.TickAsync(2);
        Assert.Equal(90, mon.History.Latest!.CpuTempC);
    }

    [Fact]
    public async Task Hidden_WithoutCoreTemp_FirstReadingAlreadyHasCpu()
    {
        var hw = new FakeHardware();
        var mon = new SensorMonitor(hw, () => new FanSettings(FanMode.Smart), new StubFast(null, 72));
        FanGuardAction? got = null;
        mon.GuardTriggered += (a, _) => got = a;
        await mon.TickAsync(2);
        Assert.NotNull(mon.History.Latest!.CpuTempC);
        Assert.Null(got);   // sem isso o FanGuard tiraria o Inteligente por "falta de sensor"
    }

    [Fact]
    public async Task Hidden_WithoutCoreTemp_ReadsBiosCpuOnlyEvery10s()
    {
        var hw = new FakeHardware();
        var mon = new SensorMonitor(hw, () => new FanSettings(FanMode.Smart), new StubFast(null, 72));
        for (long t = 1; t <= 20; t++) await mon.TickAsync(t);
        Assert.Equal(3, hw.Calls.Count(c => c == "cpuTemp"));   // primeira leitura, ticks 10 e 20
        Assert.DoesNotContain(hw.Calls, c => c == "sensors");
        Assert.NotNull(mon.History.Latest!.CpuTempC);            // reaproveita a última leitura da BIOS
    }

    [Fact]
    public async Task Visible_ReadsFullBiosSensors()
    {
        var hw = new FakeHardware();
        var mon = new SensorMonitor(hw, () => new FanSettings(FanMode.Smart), new StubFast(88, 72)) { UiVisible = true };
        await mon.TickAsync(1);
        Assert.Contains("sensors", hw.Calls);
    }

    [Fact]
    public async Task SensorFailure_RecordsUnavailable()
    {
        var hw = new FakeHardware { FailOn = op => op == "sensors" };
        var mon = new SensorMonitor(hw, () => new FanSettings(FanMode.Custom, 40, 40)) { UiVisible = true };
        FanGuardAction? got = null;
        mon.GuardTriggered += (a, _) => got = a;
        await mon.TickAsync(1);
        Assert.Equal(FanGuardAction.DisableCustom, got);
    }
}
