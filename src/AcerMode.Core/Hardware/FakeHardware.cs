namespace AcerMode.Core;

// Notebook de mentira para --demo, capturas e testes. Sensores oscilam conforme o modo.
public sealed class FakeHardware : IAcerHardware
{
    readonly object gate = new();
    public List<string> Calls { get; } = new();
    public ThermalMode Mode { get; private set; } = ThermalMode.Balanced;
    public LightingProfile? Lighting { get; private set; }
    public FanSettings Fans { get; private set; } = new();
    public Func<string, bool>? FailOn { get; set; }        // "mode", "lighting", "sensors", "fans"
    public SensorReading? NextReading { get; set; }        // força a próxima leitura
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    async Task Step(string op, string call)
    {
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay);
        lock (gate) Calls.Add(call);
        if (FailOn?.Invoke(op) == true) throw new HardwareException("Falha simulada em " + op + ".");
    }

    public async Task<int> GetModeCodeAsync() { await Step("mode", "getMode"); return (int)Mode; }
    public async Task SetModeAsync(ThermalMode mode) { await Step("mode", "mode:" + mode); Mode = mode; }
    public async Task ApplyLightingAsync(LightingProfile p) { await Step("lighting", "lighting:" + p.Effect); Lighting = p.Normalized(); }

    public async Task<SensorReading> ReadSensorsAsync()
    {
        await Step("sensors", "sensors");
        if (NextReading is { } r) { NextReading = null; return r; }
        double t = Environment.TickCount64 / 1000.0;
        int heat = Mode switch { ThermalMode.Turbo => 82, ThermalMode.Performance => 74, ThermalMode.Quiet => 58, ThermalMode.Eco => 52, _ => 66 };
        int rpm = Fans.Mode == FanMode.Max ? 5600 : Mode switch { ThermalMode.Turbo => 4800, ThermalMode.Performance => 4100, ThermalMode.Quiet => 2100, ThermalMode.Eco => 1900, _ => 3000 };
        return new SensorReading(
            heat + (int)(4 * Math.Sin(t / 3)), heat - 10 + (int)(3 * Math.Sin(t / 5 + 1)), 48,
            rpm + (int)(150 * Math.Sin(t / 2)), rpm - 300 + (int)(120 * Math.Sin(t / 4)), DateTime.Now);
    }

    public async Task<FanState> GetFansAsync() { await Step("fans", "getFans"); return new FanState(Fans.Mode, Fans.Mode, Fans.CpuPercent, Fans.GpuPercent); }
    public async Task SetFansAsync(FanSettings f) { await Step("fans", "fans:" + f.Mode); Fans = f.Normalized(); }
}
