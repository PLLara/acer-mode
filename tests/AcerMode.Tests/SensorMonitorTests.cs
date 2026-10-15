using AcerMode.Core;
using Xunit;

public class SensorMonitorTests
{
    [Theory]
    [InlineData(1, true, false, true)] [InlineData(2, true, false, true)]
    [InlineData(1, false, false, false)] [InlineData(3, false, false, false)]
    [InlineData(1, false, true, false)] [InlineData(3, false, true, true)] [InlineData(6, false, true, true)]
    public void Cadence(long tick, bool visible, bool custom, bool expected) =>
        Assert.Equal(expected, SensorMonitor.ShouldPoll(tick, visible, custom));

    [Fact]
    public async Task HotReading_TriggersGuard()
    {
        var hw = new FakeHardware { NextReading = new SensorReading(91, 60, null, null, null, DateTime.Now) };
        var mon = new SensorMonitor(hw, () => new FanSettings(FanMode.Custom, 40, 40)) { UiVisible = true };
        FanGuardAction? got = null;
        mon.GuardTriggered += (a, _) => got = a;
        await mon.TickAsync(1);
        Assert.Equal(FanGuardAction.RevertToAuto, got);
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
