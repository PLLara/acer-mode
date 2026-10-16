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
