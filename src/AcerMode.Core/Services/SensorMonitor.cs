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

    // 1 s com UI visível; 3 s escondido se há ventoinha em Personalizado; senão parado.
    public static bool ShouldPoll(long tick, bool visible, bool custom) => visible || (custom && tick % 3 == 0);

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
        if (!ShouldPoll(tick, UiVisible, fans.Mode == FanMode.Custom)) return;
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
