using AcerMode.Core;
using Xunit;

public class FanGuardTests
{
    static SensorReading R(int? cpu, int? gpu) => new(cpu, gpu, null, null, null, DateTime.Now);
    static readonly FanSettings Custom = new(FanMode.Custom, 40, 40);

    [Fact] public void IgnoresNonCustom() => Assert.Equal(FanGuardAction.None, FanGuard.Evaluate(new FanSettings(FanMode.Auto), R(99, 99)));
    [Fact] public void At85_IsFine() => Assert.Equal(FanGuardAction.None, FanGuard.Evaluate(Custom, R(85, 85)));
    [Fact] public void Cpu86_Reverts() => Assert.Equal(FanGuardAction.RevertToAuto, FanGuard.Evaluate(Custom, R(86, 50)));
    [Fact] public void Gpu86_Reverts() => Assert.Equal(FanGuardAction.RevertToAuto, FanGuard.Evaluate(Custom, R(50, 86)));
    [Fact] public void MissingTemps_DisablesCustom() => Assert.Equal(FanGuardAction.DisableCustom, FanGuard.Evaluate(Custom, R(null, 70)));
    [Fact] public void NoReading_DisablesCustom() => Assert.Equal(FanGuardAction.DisableCustom, FanGuard.Evaluate(Custom, null));

    [Fact]
    public void DirtyMarker_SetClear()
    {
        var path = Path.Combine(Path.GetTempPath(), "acermode-dirty-" + Guid.NewGuid().ToString("N"));
        var m = new DirtyMarker(path);
        Assert.False(m.Exists);
        m.Set(); Assert.True(m.Exists);
        m.Clear(); Assert.False(m.Exists);
    }
}
