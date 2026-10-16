using AcerMode.Core;
using Xunit;

public class FanCurveTests
{
    static readonly FanCurveOutput Off = FanCurveOutput.Bios;

    [Fact] public void Cool_LeavesFansToBios() => Assert.Equal(Off, FanCurve.Next(Off, 60, 55));
    [Fact] public void NoReading_LeavesFansToBios() => Assert.Equal(Off, FanCurve.Next(new FanCurveOutput(true, 80), null, null));
    [Fact] public void At70_StartsAt55() => Assert.Equal(new FanCurveOutput(true, 55), FanCurve.Next(Off, 70, 50));
    [Fact] public void At90_IsFull() => Assert.Equal(new FanCurveOutput(true, 100), FanCurve.Next(Off, 50, 90));
    [Fact] public void Above90_StaysFull() => Assert.Equal(new FanCurveOutput(true, 100), FanCurve.Next(Off, 95, 74));
    [Fact] public void UsesHotterChip() => Assert.Equal(FanCurve.Next(Off, 80, 50), FanCurve.Next(Off, 50, 80));
    [Fact] public void StepsOf5() => Assert.Equal(0, FanCurve.Next(Off, 77, 40).Percent % 5);

    [Fact] public void RampsUpImmediately() =>
        Assert.Equal(new FanCurveOutput(true, 100), FanCurve.Next(new FanCurveOutput(true, 55), 92, 60));

    [Fact] public void RampsDownSlowly() =>
        Assert.Equal(new FanCurveOutput(true, 95), FanCurve.Next(new FanCurveOutput(true, 100), 72, 60));

    [Fact] public void Hysteresis_KeepsBoostBetween64And70() =>
        Assert.True(FanCurve.Next(new FanCurveOutput(true, 55), 67, 60).Boost);

    [Fact] public void Hysteresis_NotStartedBelow70() =>
        Assert.False(FanCurve.Next(Off, 69, 60).Boost);

    [Fact] public void BelowStopTemp_ReturnsToBios() =>
        Assert.Equal(Off, FanCurve.Next(new FanCurveOutput(true, 55), 64, 60));
}
