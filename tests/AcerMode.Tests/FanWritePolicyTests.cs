using AcerMode.Core;
using Xunit;

public class FanWritePolicyTests
{
    static FanCurveOutput B(int p) => new(true, p);
    static readonly TimeSpan Now = TimeSpan.Zero, Later = TimeSpan.FromSeconds(25);

    [Fact] public void Toggle_IsAlwaysWritten() => Assert.True(FanWritePolicy.ShouldWrite(FanCurveOutput.Bios, B(55), Now));
    [Fact] public void BackToBios_WaitsAtLeast30s() => Assert.False(FanWritePolicy.ShouldWrite(B(55), FanCurveOutput.Bios, TimeSpan.FromSeconds(4)));
    [Fact] public void BackToBios_After30s_IsWritten() => Assert.True(FanWritePolicy.ShouldWrite(B(55), FanCurveOutput.Bios, TimeSpan.FromSeconds(31)));
    [Fact] public void SmallRise_IsSkipped() => Assert.False(FanWritePolicy.ShouldWrite(B(55), B(60), Now));
    [Fact] public void BigRise_IsWrittenNow() => Assert.True(FanWritePolicy.ShouldWrite(B(55), B(65), Now));
    [Fact] public void ReachingFull_IsWrittenNow() => Assert.True(FanWritePolicy.ShouldWrite(B(95), B(100), Now));
    [Fact] public void Drop_WaitsForHold() => Assert.False(FanWritePolicy.ShouldWrite(B(100), B(80), Now));
    [Fact] public void Drop_AfterHold_IsWritten() => Assert.True(FanWritePolicy.ShouldWrite(B(100), B(80), Later));
    [Fact] public void SmallDrop_IsSkippedEvenLater() => Assert.False(FanWritePolicy.ShouldWrite(B(100), B(95), Later));
    [Fact] public void Same_IsSkipped() => Assert.False(FanWritePolicy.ShouldWrite(B(70), B(70), Later));
}
