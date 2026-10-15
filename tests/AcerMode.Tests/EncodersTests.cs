using AcerMode.Core;
using Xunit;

public class EncodersTests
{
    [Fact] public void ThermalSet_Turbo() => Assert.Equal(0x050BUL, Encoders.ThermalSet(ThermalMode.Turbo));
    [Fact] public void ThermalParse_ProbeValue() => Assert.Equal(5, Encoders.ThermalParse(0xA2D0500));
    [Fact] public void ZoneColor_Zone1Red() => Assert.Equal(0x0000FF01UL, Encoders.ZoneColor(0, new Rgb(255, 0, 0)));
    [Fact] public void ZoneColor_Zone4() => Assert.Equal(0x0A2DFF08UL, Encoders.ZoneColor(3, new Rgb(255, 45, 10)));
    [Fact] public void ZoneColorParse_ProbeValue() => Assert.Equal(new Rgb(255, 45, 10), Encoders.ZoneColorParse(0xA2DFF00));
    [Fact] public void ZoneColor_RejectsZone4() => Assert.Throws<ArgumentOutOfRangeException>(() => Encoders.ZoneColor(4, default));

    [Fact]
    public void Backlight_StaticMatchesLegacyPayload()
    {
        // Mesmo payload que o AcerMode.cs antigo manda para o Turbo.
        var p = LightingProfile.Solid(new Rgb(255, 45, 10), 100);
        Assert.Equal(new byte[] { 0, 0, 100, 0, 0, 255, 45, 10, 3, 1, 0, 0, 0, 0, 0, 0 }, Encoders.Backlight(p));
    }

    [Fact]
    public void Backlight_WaveCarriesSpeedAndDirection()
    {
        var p = new LightingProfile { Effect = KbEffect.Wave, Speed = 7, Brightness = 80, Direction = KbDirection.LeftToRight, Color = new Rgb(1, 2, 3) };
        Assert.Equal(new byte[] { 3, 7, 80, 0, 2, 1, 2, 3, 3, 1, 0, 0, 0, 0, 0, 0 }, Encoders.Backlight(p));
    }

    [Fact]
    public void Backlight_ClampsOutOfRange()
    {
        var p = new LightingProfile { Effect = KbEffect.Breathing, Speed = 42, Brightness = 300 };
        var b = Encoders.Backlight(p);
        Assert.Equal(9, b[1]);
        Assert.Equal(100, b[2]);
    }

    [Fact] public void SensorQuery_CpuTemp() => Assert.Equal(0x0101u, Encoders.SensorQuery(Sensor.CpuTemp));
    [Fact] public void SensorQuery_GpuTemp() => Assert.Equal(0x0A01u, Encoders.SensorQuery(Sensor.GpuTemp));

    [Fact]
    public void SensorParse_ProbeValues()
    {
        Assert.Equal(85, Encoders.SensorParse(0x5500));
        Assert.Equal(3750, Encoders.SensorParse(0xEA600));
        Assert.Null(Encoders.SensorParse(0x5501));   // status != 0
    }

    [Theory]
    [InlineData(FanMode.Auto, 0x00410009UL)]
    [InlineData(FanMode.Max, 0x00820009UL)]
    [InlineData(FanMode.Custom, 0x00C30009UL)]
    public void FanBehaviorSet_BothFans(FanMode m, ulong expected) => Assert.Equal(expected, Encoders.FanBehaviorSet(m, m));

    [Fact] public void FanBehaviorParse_ProbeValue() => Assert.Equal((FanMode.Auto, FanMode.Auto), Encoders.FanBehaviorParse(0x4100));
    [Fact] public void FanBehaviorParse_RoundTrip() => Assert.Equal((FanMode.Custom, FanMode.Max), Encoders.FanBehaviorParse((0x80UL | 0x03UL) << 8));
    [Fact] public void FanSpeedSet_Gpu60() => Assert.Equal(0x3C04UL, Encoders.FanSpeedSet(Fan.Gpu, 60));

    [Fact]
    public void FanSpeedParse_ProbeValues()
    {
        Assert.Equal(70, Encoders.FanSpeedParse(0x4600));
        Assert.Null(Encoders.FanSpeedParse(0x3C01));
    }

    [Fact]
    public void IsModeKeyEvent()
    {
        Assert.True(Encoders.IsModeKeyEvent(new byte[] { 7, 5, 1, 0 }));
        Assert.False(Encoders.IsModeKeyEvent(new byte[] { 9, 0, 0 }));
        Assert.False(Encoders.IsModeKeyEvent(null));
    }
}
