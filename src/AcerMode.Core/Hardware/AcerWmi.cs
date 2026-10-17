using System.Collections.Concurrent;
using System.Management;

namespace AcerMode.Core;

// Uma thread executa as chamadas à BIOS, uma por vez (chamadas simultâneas travam a BIOS,
// principalmente ao voltar da suspensão). Se uma chamada passa do limite, a thread é
// abandonada, outra assume e as próximas falham na hora durante a pausa.
public sealed class SerialWorker : IDisposable
{
    readonly Func<long> now;
    readonly long backoffMs;
    readonly object gate = new();
    BlockingCollection<Action> queue = null!;
    long failUntil = long.MinValue;

    // Marca da ação em execução nesta thread; vira true quando a ação passa do limite.
    [ThreadStatic] static System.Runtime.CompilerServices.StrongBox<bool>? current;

    // Chamada antes de cada acesso à BIOS: uma ação abandonada por timeout não continua
    // escrevendo depois que a BIOS volta (nem em paralelo com o worker novo).
    public static void ThrowIfAbandoned()
    {
        if (current?.Value == true) throw new HardwareException("Chamada à BIOS abandonada depois do limite de tempo.");
    }

    public SerialWorker(Func<long>? clock = null, long backoffMs = 30_000)
    {
        now = clock ?? (() => Environment.TickCount64);
        this.backoffMs = backoffMs;
        StartThread();
    }

    void StartThread()
    {
        var q = new BlockingCollection<Action>();
        var t = new Thread(() => { foreach (var a in q.GetConsumingEnumerable()) a(); }) { IsBackground = true, Name = "AcerWmi" };
        t.Start();
        queue = q;
    }

    public async Task<T> Run<T>(Func<T> f, TimeSpan timeout)
    {
        BlockingCollection<Action> q;
        lock (gate)
        {
            if (now() < failUntil) throw new HardwareException("A BIOS não respondeu. Nova tentativa em instantes.");
            q = queue;
        }
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var abandoned = new System.Runtime.CompilerServices.StrongBox<bool>(false);
        q.Add(() =>
        {
            if (tcs.Task.IsCompleted) return;
            current = abandoned;
            try { tcs.TrySetResult(f()); }
            catch (HardwareException ex) { tcs.TrySetException(ex); }
            catch (Exception ex) { tcs.TrySetException(new HardwareException(ex.Message)); }
            finally { current = null; }
        });
        if (await Task.WhenAny(tcs.Task, Task.Delay(timeout)) != tcs.Task)
        {
            abandoned.Value = true;
            lock (gate)
            {
                failUntil = now() + backoffMs;
                if (ReferenceEquals(q, queue)) { q.CompleteAdding(); StartThread(); }
            }
            tcs.TrySetException(new HardwareException("A BIOS não respondeu."));
        }
        return await tcs.Task;
    }

    public void Dispose() { lock (gate) queue.CompleteAdding(); }
}

public sealed class AcerWmi : IAcerHardware, IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);
    readonly SerialWorker worker = new();
    ManagementObject? obj;   // só tocado pela thread do worker

    ManagementObject Obj()
    {
        if (obj is not null) return obj;
        using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM AcerGamingFunction");
        foreach (ManagementObject o in s.Get()) return obj = o;
        throw new HardwareException("Interface AcerGamingFunction não encontrada.");
    }

    ulong Call(string method, object input)
    {
        SerialWorker.ThrowIfAbandoned();
        try
        {
            var o = Obj();
            var p = o.GetMethodParameters(method);
            p["gmInput"] = input;
            var r = o.InvokeMethod(method, p, null);
            return Convert.ToUInt64(r["gmOutput"]);
        }
        catch (ManagementException) { obj = null; throw; }
    }

    void SetChecked(string method, object input, string what)
    {
        ulong status = Call(method, input);
        if (!Encoders.StatusOk(status)) throw new HardwareException($"A BIOS recusou {what} (status {status}).");
    }

    Task<T> Run<T>(Func<T> f) => worker.Run(f, Timeout);
    Task Run(Action a) => worker.Run(() => { a(); return true; }, Timeout);

    public Task<int> GetModeCodeAsync() => Run(() => Encoders.ThermalParse(Call("GetGamingMiscSetting", Encoders.ThermalSetting)));

    public Task SetModeAsync(ThermalMode mode) => Run(() => SetChecked("SetGamingMiscSetting", Encoders.ThermalSet(mode), "a troca de modo"));

    public Task ApplyLightingAsync(LightingProfile profile) => Run(() =>
    {
        var p = profile.Normalized();
        if (p.Effect == KbEffect.Static)
            for (int z = 0; z < LightingProfile.ZoneCount; z++)
                SetChecked("SetGamingRgbKb", Encoders.ZoneColor(z, p.Zones[z]), "a cor da zona " + (z + 1));
        SetChecked("SetGamingKBBacklight", Encoders.Backlight(p), "o efeito " + KbEffects.Label(p.Effect));
    });

    public Task<SensorReading> ReadSensorsAsync() => Run(() =>
    {
        int? Read(Sensor s) => SensorReading.Plausible(s, Encoders.SensorParse(Call("GetGamingSysInfo", Encoders.SensorQuery(s))));
        return new SensorReading(Read(Sensor.CpuTemp), Read(Sensor.GpuTemp), Read(Sensor.SystemTemp),
            Read(Sensor.CpuFanRpm), Read(Sensor.GpuFanRpm), DateTime.Now);
    });

    public Task<int?> ReadCpuTempAsync() => Run(() =>
        SensorReading.Plausible(Sensor.CpuTemp, Encoders.SensorParse(Call("GetGamingSysInfo", Encoders.SensorQuery(Sensor.CpuTemp)))));

    public Task<FanState> GetFansAsync() => Run(() =>
    {
        var b = Encoders.FanBehaviorParse(Call("GetGamingFanBehavior", Encoders.BothFans))
                ?? throw new HardwareException("A BIOS não informou o modo das ventoinhas.");
        return new FanState(b.Cpu, b.Gpu,
            Encoders.FanSpeedParse(Call("GetGamingFanSpeed", (uint)Fan.Cpu)),
            Encoders.FanSpeedParse(Call("GetGamingFanSpeed", (uint)Fan.Gpu)));
    });

    // % primeiro, depois o modo; em seguida lê de volta para confirmar.
    public Task SetFansAsync(FanSettings fans) => Run(() =>
    {
        var f = fans.Normalized();
        if (f.Mode == FanMode.Smart) f = f with { Mode = FanMode.Auto };   // Inteligente é do app; a BIOS só conhece 1–3
        if (f.Mode == FanMode.Custom)
        {
            SetChecked("SetGamingFanSpeed", Encoders.FanSpeedSet(Fan.Cpu, f.CpuPercent), "a velocidade da ventoinha da CPU");
            SetChecked("SetGamingFanSpeed", Encoders.FanSpeedSet(Fan.Gpu, f.GpuPercent), "a velocidade da ventoinha da GPU");
        }
        SetChecked("SetGamingFanBehavior", Encoders.FanBehaviorSet(f.Mode, f.Mode), "o modo das ventoinhas");
        var back = Encoders.FanBehaviorParse(Call("GetGamingFanBehavior", Encoders.BothFans));
        if (back is not { } b || b.Cpu != f.Mode || b.Gpu != f.Mode)
            throw new HardwareException("A BIOS não confirmou o modo das ventoinhas.");
    });

    public Task SetFanSpeedsAsync(int cpuPercent, int gpuPercent) => Run(() =>
    {
        SetChecked("SetGamingFanSpeed", Encoders.FanSpeedSet(Fan.Cpu, Math.Clamp(cpuPercent, FanSettings.MinPercent, 100)), "a velocidade da ventoinha da CPU");
        SetChecked("SetGamingFanSpeed", Encoders.FanSpeedSet(Fan.Gpu, Math.Clamp(gpuPercent, FanSettings.MinPercent, 100)), "a velocidade da ventoinha da GPU");
    });

    // Somente leitura: respostas cruas de todos os Get* conhecidos.
    public Task<IReadOnlyList<string>> ProbeAsync() => Run<IReadOnlyList<string>>(() =>
    {
        var lines = new List<string>();
        void Q(string m, object input)
        {
            try { lines.Add($"{m}(0x{Convert.ToUInt64(input):X}) = 0x{Call(m, input):X}"); }
            catch (Exception ex) { lines.Add($"{m}(0x{Convert.ToUInt64(input):X}) ERRO {ex.Message}"); }
        }
        Q("GetGamingMiscSetting", Encoders.ThermalSetting);
        foreach (var s in Enum.GetValues<Sensor>()) Q("GetGamingSysInfo", Encoders.SensorQuery(s));
        Q("GetGamingFanBehavior", Encoders.BothFans);
        Q("GetGamingFanSpeed", (uint)Fan.Cpu);
        Q("GetGamingFanSpeed", (uint)Fan.Gpu);
        for (uint z = 1; z <= 8; z <<= 1) Q("GetGamingRgbKb", z);
        return lines;
    });

    public void Dispose() => worker.Dispose();
}
