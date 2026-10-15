namespace AcerMode.Core;

public enum Sensor : byte { CpuTemp = 0x01, CpuFanRpm = 0x02, SystemTemp = 0x03, GpuFanRpm = 0x06, GpuTemp = 0x0A }

public sealed record SensorReading(int? CpuTempC, int? GpuTempC, int? SystemTempC, int? CpuFanRpm, int? GpuFanRpm, DateTime At)
{
    public bool HasTemps => CpuTempC is not null && GpuTempC is not null;

    // Leitura fora da faixa física é tratada como indisponível, nunca como dado.
    public static int? Plausible(Sensor s, int? raw) => raw switch
    {
        null => null,
        int v when s is Sensor.CpuTemp or Sensor.GpuTemp or Sensor.SystemTemp => v is >= 1 and <= 120 ? v : null,
        int v => v is >= 0 and <= 9000 ? v : null,
    };
}

public sealed class SensorHistory
{
    readonly SensorReading[] items;
    int start, count;
    readonly object gate = new();

    public SensorHistory(int capacity = 120) => items = new SensorReading[capacity];

    public void Add(SensorReading r)
    {
        lock (gate)
        {
            items[(start + count) % items.Length] = r;
            if (count < items.Length) count++; else start = (start + 1) % items.Length;
        }
    }

    public SensorReading? Latest { get { lock (gate) return count == 0 ? null : items[(start + count - 1) % items.Length]; } }

    public IReadOnlyList<SensorReading> Snapshot()
    {
        lock (gate)
        {
            var list = new SensorReading[count];
            for (int i = 0; i < count; i++) list[i] = items[(start + i) % items.Length];
            return list;
        }
    }
}
