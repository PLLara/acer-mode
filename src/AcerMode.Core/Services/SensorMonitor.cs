namespace AcerMode.Core;

// Lê sensores na cadência certa e alimenta o FanGuard.
public sealed class SensorMonitor : IDisposable
{
    readonly IAcerHardware hw;
    readonly Func<FanSettings> activeFans;
    readonly CancellationTokenSource cts = new();

    public SensorMonitor(IAcerHardware hw, Func<FanSettings> activeFans) { this.hw = hw; this.activeFans = activeFans; }

    public bool UiVisible { get; set; }
    public SensorHistory History { get; } = new(120);
    public event Action<SensorReading>? Updated;
    public event Action<FanGuardAction, SensorReading?>? GuardTriggered;

    // 1 s com UI visível; escondido: 2 s no Inteligente (a curva precisa acompanhar),
    // 3 s no Personalizado (o FanGuard precisa de leitura); nos demais, parado.
    public static bool ShouldPoll(long tick, bool visible, FanMode mode) =>
        visible || mode switch { FanMode.Smart => tick % 2 == 0, FanMode.Custom => tick % 3 == 0, _ => false };

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
        if (!ShouldPoll(tick, UiVisible, fans.Mode)) return;
        SensorReading? r = null;
        try
        {
            r = await hw.ReadSensorsAsync();
            History.Add(r);
            Updated?.Invoke(r);
        }
        catch (HardwareException) { }
        var action = FanGuard.Evaluate(fans, r);
        if (action != FanGuardAction.None) GuardTriggered?.Invoke(action, r);
    }

    public void Dispose() => cts.Cancel();
}
