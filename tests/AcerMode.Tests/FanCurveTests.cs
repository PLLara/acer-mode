using AcerMode.Core;
using Xunit;

public class FanCurveTests
{
    static readonly FanCurveOutput Off = FanCurveOutput.Bios;

    // alvo da curva, sem o limite de subida (parte de 100% para descer devagar não interferir)
    static int Target(int c) => FanCurve.TargetPercent(c);

    [Fact] public void Cool_LeavesFansToBios() => Assert.Equal(Off, FanCurve.Next(Off, 60, 55));
    [Fact] public void NoReading_LeavesFansToBios() => Assert.Equal(Off, FanCurve.Next(new FanCurveOutput(true, 80), null, null));

    [Theory]
    [InlineData(70, 40)] [InlineData(75, 50)] [InlineData(80, 60)] [InlineData(84, 70)]
    [InlineData(88, 80)] [InlineData(92, 90)] [InlineData(95, 100)] [InlineData(99, 100)]
    public void Points(int temp, int percent) => Assert.Equal(percent, Target(temp));

    [Fact] public void At70_StartsAt40() => Assert.Equal(new FanCurveOutput(true, 40), FanCurve.Next(Off, 70, 50));
    [Fact] public void UsesHotterChip() => Assert.Equal(FanCurve.Next(Off, 80, 50), FanCurve.Next(Off, 50, 80));
    [Fact] public void StepsOf5() => Assert.Equal(0, FanCurve.Next(Off, 77, 40).Percent % 5);

    [Fact] public void RampsUpGradually() =>
        Assert.Equal(new FanCurveOutput(true, 50), FanCurve.Next(new FanCurveOutput(true, 40), 88, 60));

    [Fact] public void FirstBoost_AlsoRampsFrom40() =>
        Assert.Equal(new FanCurveOutput(true, 40), FanCurve.Next(Off, 86, 60));

    [Fact] public void Emergency_JumpsStraightToTarget() =>
        Assert.Equal(new FanCurveOutput(true, 100), FanCurve.Next(new FanCurveOutput(true, 40), 95, 74));

    [Fact] public void RampsDownSlowly() =>
        Assert.Equal(new FanCurveOutput(true, 95), FanCurve.Next(new FanCurveOutput(true, 100), 72, 60));

    [Fact] public void Hysteresis_KeepsBoostBetween64And70() =>
        Assert.True(FanCurve.Next(new FanCurveOutput(true, 40), 67, 60).Boost);

    [Fact] public void Hysteresis_NotStartedBelow70() =>
        Assert.False(FanCurve.Next(Off, 69, 60).Boost);

    [Fact] public void BelowStopTemp_ReturnsToBios() =>
        Assert.Equal(Off, FanCurve.Next(new FanCurveOutput(true, 40), 64, 60));
}
