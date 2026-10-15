using AcerMode.Core;
using Xunit;

public class FakeHardwareTests
{
    [Fact]
    public async Task RecordsCallsAndState()
    {
        var hw = new FakeHardware();
        await hw.SetModeAsync(ThermalMode.Turbo);
        Assert.Equal(5, await hw.GetModeCodeAsync());
        Assert.Contains("mode:Turbo", hw.Calls);
    }

    [Fact]
    public async Task FailOnThrowsHardwareException()
    {
        var hw = new FakeHardware { FailOn = op => op == "lighting" };
        await Assert.ThrowsAsync<HardwareException>(() => hw.ApplyLightingAsync(new LightingProfile()));
    }
}
