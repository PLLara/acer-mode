using AcerMode.Core;
using Xunit;

public class SensorsTests
{
    [Theory]
    [InlineData(Sensor.CpuTemp, 85, 85)] [InlineData(Sensor.CpuTemp, 0, null)] [InlineData(Sensor.GpuTemp, 130, null)]
    [InlineData(Sensor.CpuFanRpm, 3750, 3750)] [InlineData(Sensor.CpuFanRpm, 0, 0)] [InlineData(Sensor.GpuFanRpm, 65535, null)]
    public void Plausible(Sensor s, int raw, int? expected) => Assert.Equal(expected, SensorReading.Plausible(s, raw));

    [Fact]
    public void History_KeepsLastN()
    {
        var h = new SensorHistory(3);
        for (int i = 1; i <= 5; i++) h.Add(new SensorReading(i, i, null, null, null, DateTime.MinValue.AddSeconds(i)));
        Assert.Equal(new int?[] { 3, 4, 5 }, h.Snapshot().Select(r => r.CpuTempC));
        Assert.Equal(5, h.Latest!.CpuTempC);
    }
}
