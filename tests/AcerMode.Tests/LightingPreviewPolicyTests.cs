using AcerMode.Core;
using Xunit;

public class LightingPreviewPolicyTests
{
    [Fact] public void SameMode_Live() => Assert.True(LightingPreviewPolicy.ShouldSendLive(ThermalMode.Turbo, ThermalMode.Turbo, false));
    [Fact] public void OtherMode_NotLive() => Assert.False(LightingPreviewPolicy.ShouldSendLive(ThermalMode.Eco, ThermalMode.Turbo, false));
    [Fact] public void UnknownActive_NotLive() => Assert.False(LightingPreviewPolicy.ShouldSendLive(ThermalMode.Eco, null, false));
    [Fact] public void Independent_AlwaysLive() => Assert.True(LightingPreviewPolicy.ShouldSendLive(ThermalMode.Eco, ThermalMode.Turbo, true));
}
