namespace AcerMode.Core;

// Lê sensores na cadência certa e alimenta o FanGuard e a curva do Inteligente.
// Janela aberta: leitura completa pela BIOS (temperaturas e RPM). Escondida (quando se joga):
// só fontes baratas (Core Temp e NVML), porque cada leitura pela BIOS congela os núcleos por
// até ~19 ms e causava stutter. Sem Core Temp, a CPU vem da BIOS no máximo a cada 10 s.
public sealed class SensorMonitor : IDisposable
{
    const int BiosCpuFallbackTicks = 10;

    readonly IAcerHardware hw;
    readonly Func<FanSettings> activeFans;
    readonly IFastTemps? fast;
    readonly CancellationTokenSource cts = new();
    int? lastBiosCpu;

    public SensorMonitor(IAcerHardware hw, Func<FanSettings> activeFans, IFastTemps? fast = null)
    {
        this.hw = hw; this.activeFans = activeFans; this.fast = fast;
    }

    public bool UiVisible { get; set; }
    public SensorHistory History { get; } = new(120);
    public event Action<SensorReading>? Updated;
    public event Action<FanGuardAction, SensorReading?>? GuardTriggered;

    // Overclock ligado: a proteção de 85 °C da GPU precisa de leitura em qualquer modo de ventoinha.
    public Func<bool>? OverclockActive { get; set; }

    // 1 s com UI visível; escondido: 2 s no Inteligente (a curva precisa acompanhar) ou com
    // overclock ligado, 3 s no Personalizado (o FanGuard precisa de leitura); senão, parado.
    public static bool ShouldPoll(long tick, bool visible, FanMode mode, bool overclockActive = false) =>
        visible || (overclockActive && tick % 2 == 0)
        || mode switch { FanMode.Smart => tick % 2 == 0, FanMode.Custom => tick % 3 == 0, _ => false };

    public void Start() => _ = LoopAsync();

    async Task LoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        long tick = 0;
        try { while (await timer.WaitForNextTickAsync(cts.Token)) await TickAsync(++tick); }
        catch (OperationCanceledException) { }
    }

    public async Task TickAsync(long tick)
    {
        var fans = activeFans();
        if (!ShouldPoll(tick, UiVisible, fans.Mode, OverclockActive?.Invoke() == true)) return;
        SensorReading? r = null;
        try
        {
            r = UiVisible || fast is null ? await hw.ReadSensorsAsync() : await ReadFastAsync(tick);
            if (r.HasTemps || r.CpuTempC is not null || r.GpuTempC is not null)
            {
                History.Add(r);
                Updated?.Invoke(r);
            }
        }
        catch (HardwareException) { }
        var action = FanGuard.Evaluate(fans, r);
        if (action != FanGuardAction.None) GuardTriggered?.Invoke(action, r);
    }

    // O núcleo mais quente do Core Temp pula 60↔80 °C entre leituras (a BIOS já entrega um
    // valor suavizado). Média móvel exponencial: constante de tempo ~7 s com leituras de 2 s.
    const double CpuSmoothing = 0.25;
    double? cpuEma;

    async Task<SensorReading> ReadFastAsync(long tick)
    {
        int? cpu = fast!.CpuC();
        if (cpu is { } raw)
        {
            cpuEma = cpuEma is { } e ? e + CpuSmoothing * (raw - e) : raw;
            cpu = (int)Math.Round(cpuEma.Value);
        }
        else
        {
            // a primeira leitura não pode esperar 10 s: sem CPU o FanGuard desligaria o Inteligente
            if (lastBiosCpu is null || tick % BiosCpuFallbackTicks == 0) lastBiosCpu = await hw.ReadCpuTempAsync();
            cpu = lastBiosCpu;
        }
        return new SensorReading(cpu, fast.GpuC(), null, null, null, DateTime.Now);
    }

    public void Dispose() => cts.Cancel();
}
