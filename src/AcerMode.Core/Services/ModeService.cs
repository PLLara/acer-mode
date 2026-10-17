namespace AcerMode.Core;

public sealed record ModeChangeResult(ThermalMode Mode, IReadOnlyList<string> Warnings);

// Troca de modo como transação: modo térmico → iluminação → ventoinhas.
// Se o modo falha, nada mais é aplicado; falha de iluminação/ventoinha vira aviso.
public sealed class ModeService
{
    readonly IAcerHardware hw;
    readonly Func<AppSettings> settings;
    readonly DirtyMarker fansDirty;
    readonly AppLog? log;
    ThermalMode beforeTurbo = ThermalMode.Balanced;

    readonly Func<DateTime> clock;

    public ModeService(IAcerHardware hw, Func<AppSettings> settings, DirtyMarker fansDirty, AppLog? log = null, Func<DateTime>? clock = null)
    {
        this.hw = hw; this.settings = settings; this.fansDirty = fansDirty; this.log = log;
        this.clock = clock ?? (() => DateTime.Now);
    }

    public ThermalMode? Active { get; private set; }

    // Modo para onde o app está indo: muda no início da transação, antes de Active.
    public ThermalMode? Target { get; private set; }
    public FanSettings ActiveFans { get; private set; } = new();
    public event Action<ThermalMode>? ModeChanged;

    public async Task<ThermalMode?> RefreshAsync()
    {
        int code = await hw.GetModeCodeAsync();
        Active = ThermalModes.IsKnown(code) ? (ThermalMode)code : null;
        Target = Active;
        return Active;
    }

    // applyFans = false no --set da linha de comando: sem o app aberto, nada vigiaria um
    // Personalizado (FanGuard).
    public async Task<ModeChangeResult> ApplyModeAsync(ThermalMode mode, bool applyFans = true)
    {
        var previous = Target;
        Target = mode;
        try { await hw.SetModeAsync(mode); }
        catch { Target = previous; throw; }
        Active = mode;
        log?.Write("modo -> " + mode);
        var warnings = await ApplyProfileAsync(mode, applyFans);
        ModeChanged?.Invoke(mode);
        return new ModeChangeResult(mode, warnings);
    }

    // Botão físico: liga o Turbo; apertar de novo volta ao modo de antes.
    public async Task<ModeChangeResult?> ToggleTurboAsync()
    {
        var current = await RefreshAsync();
        if (current == ThermalMode.Turbo) return await ApplyModeAsync(beforeTurbo);
        beforeTurbo = current ?? ThermalMode.Balanced;
        return await ApplyModeAsync(ThermalMode.Turbo);
    }

    // Prévia do editor. forMode = perfil editado (null quando a iluminação é independente);
    // se o modo-alvo mudou nesse meio-tempo (botão físico), a prévia é descartada.
    public async Task<bool> PreviewLightingAsync(LightingProfile p, ThermalMode? forMode)
    {
        if (forMode is { } m && Target != m) return false;
        await hw.ApplyLightingAsync(p);
        return true;
    }

    public async Task<IReadOnlyList<string>> ApplyFansAsync(FanSettings fans)
    {
        var w = new List<string>();
        await ApplyFansCoreAsync(fans, w);
        return w;
    }

    public async Task RevertFansToAutoAsync()
    {
        var auto = new FanSettings(FanMode.Auto, ActiveFans.CpuPercent, ActiveFans.GpuPercent);
        await hw.SetFansAsync(auto);
        ActiveFans = auto;
        curve = applied = FanCurveOutput.Bios;
        fansDirty.Clear();
        log?.Write("ventoinhas -> Auto");
    }

    public async Task RecoverAfterCrashAsync()
    {
        if (!fansDirty.Exists) return;
        log?.Write("ventoinhas em Personalizado na última sessão; voltando ao automático");
        await RevertFansToAutoAsync();
    }

    // Depois de iniciar ou voltar da suspensão: o firmware às vezes esquece a iluminação.
    public async Task<IReadOnlyList<string>> ReapplyActiveAsync()
    {
        var mode = await RefreshAsync();
        return mode is { } m ? await ApplyProfileAsync(m) : Array.Empty<string>();
    }

    async Task<IReadOnlyList<string>> ApplyProfileAsync(ThermalMode mode, bool applyFans = true)
    {
        var s = settings();
        var warnings = new List<string>();
        try { await hw.ApplyLightingAsync(s.LightingFor(mode)); }
        catch (Exception ex) { warnings.Add("Iluminação: " + ex.Message); log?.Write("ERRO iluminação: " + ex.Message); }
        if (applyFans) await ApplyFansCoreAsync(s.Profile(mode).Fans, warnings);
        return warnings;
    }

    // ---- modo Inteligente ----
    FanCurveOutput curve = FanCurveOutput.Bios;     // estado da curva (anda a cada leitura)
    FanCurveOutput applied = FanCurveOutput.Bios;   // o que está de fato na BIOS
    DateTime lastWrite = DateTime.MinValue;

    // Cada leitura de sensor passa por aqui; só age com o Inteligente ativo. A curva anda a cada
    // leitura, mas a BIOS só é escrita quando a FanWritePolicy deixa (cada escrita congela núcleos).
    public async Task OnReadingAsync(SensorReading r)
    {
        if (ActiveFans.Mode != FanMode.Smart) return;
        curve = FanCurve.Next(curve, r.CpuTempC, r.GpuTempC);
        var next = curve;
        if (!FanWritePolicy.ShouldWrite(applied, next, clock() - lastWrite)) return;
        try
        {
            if (next.Boost && applied.Boost)
                await hw.SetFanSpeedsAsync(next.Percent, next.Percent);   // já em Personalizado: só o %
            else if (next.Boost)
            {
                fansDirty.Set();
                await hw.SetFansAsync(new FanSettings(FanMode.Custom, next.Percent, next.Percent));
            }
            else
            {
                await hw.SetFansAsync(new FanSettings(FanMode.Auto));
                fansDirty.Clear();
            }
            if (next.Boost != applied.Boost) log?.Write(next.Boost ? $"Inteligente: assumiu ({next.Percent}%)" : "Inteligente: devolveu à BIOS");
            applied = next;
            lastWrite = clock();
        }
        catch (HardwareException ex) { log?.Write("ERRO Inteligente: " + ex.Message); }   // tenta de novo na próxima leitura
    }

    async Task ApplyFansCoreAsync(FanSettings fans, List<string> warnings)
    {
        try
        {
            // Inteligente começa com a BIOS no Auto; a curva assume na primeira leitura quente.
            var hwFans = fans.Mode == FanMode.Smart ? fans with { Mode = FanMode.Auto } : fans;
            if (hwFans.Mode == FanMode.Custom) fansDirty.Set();
            await hw.SetFansAsync(hwFans);
            ActiveFans = fans;
            curve = applied = FanCurveOutput.Bios;
            if (hwFans.Mode != FanMode.Custom) fansDirty.Clear();
        }
        catch (Exception ex)
        {
            warnings.Add("Ventoinhas: " + ex.Message + " Voltando ao automático.");
            log?.Write("ERRO ventoinhas: " + ex.Message);
            try { await RevertFansToAutoAsync(); }
            catch (Exception ex2)
            {
                // A escrita do Personalizado pode ter chegado à BIOS: mantém o FanGuard armado
                // (e o marcador em disco) até um Auto ser confirmado.
                log?.Write("ERRO ao voltar ventoinhas: " + ex2.Message);
                if (fans.Mode == FanMode.Custom) ActiveFans = fans;
            }
        }
    }
}
