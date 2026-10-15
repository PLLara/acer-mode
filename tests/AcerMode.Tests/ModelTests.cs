using AcerMode.Core;
using Xunit;

public class ModelTests
{
    [Theory]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(4, true)] [InlineData(5, true)] [InlineData(6, true)]
    [InlineData(2, false)] [InlineData(3, false)] [InlineData(255, false)]
    public void IsKnown(int code, bool known) => Assert.Equal(known, ThermalModes.IsKnown(code));

    [Fact]
    public void TryParse_IgnoresCase()
    {
        Assert.True(ThermalModes.TryParse("tUrBo", out var m));
        Assert.Equal(ThermalMode.Turbo, m);
        Assert.False(ThermalModes.TryParse("ludicrous", out _));
    }

    [Fact]
    public void Rgb_HexRoundTrip()
    {
        var c = Rgb.FromHex("#ff2d0a");
        Assert.Equal(new Rgb(255, 45, 10), c);
        Assert.Equal("#FF2D0A", c.ToHex());
        Assert.False(Rgb.TryFromHex("#12345", out _));
        Assert.False(Rgb.TryFromHex(null, out _));
    }
}
