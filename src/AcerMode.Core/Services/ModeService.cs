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

    public ModeService(IAcerHardware hw, Func<AppSettings> settings, DirtyMarker fansDirty, AppLog? log = null)
    {
        this.hw = hw; this.settings = settings; this.fansDirty = fansDirty; this.log = log;
    }

    public ThermalMode? Active { get; private set; }
    public FanSettings ActiveFans { get; private set; } = new();
    public event Action<ThermalMode>? ModeChanged;

    public async Task<ThermalMode?> RefreshAsync()
    {
        int code = await hw.GetModeCodeAsync();
        Active = ThermalModes.IsKnown(code) ? (ThermalMode)code : null;
        return Active;
    }

    public async Task<ModeChangeResult> ApplyModeAsync(ThermalMode mode)
    {
        await hw.SetModeAsync(mode);
        Active = mode;
        log?.Write("modo -> " + mode);
        var warnings = await ApplyProfileAsync(mode);
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

    public Task PreviewLightingAsync(LightingProfile p) => hw.ApplyLightingAsync(p);

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

    async Task<IReadOnlyList<string>> ApplyProfileAsync(ThermalMode mode)
    {
        var s = settings();
        var warnings = new List<string>();
        try { await hw.ApplyLightingAsync(s.LightingFor(mode)); }
        catch (Exception ex) { warnings.Add("Iluminação: " + ex.Message); log?.Write("ERRO iluminação: " + ex.Message); }
        await ApplyFansCoreAsync(s.Profile(mode).Fans, warnings);
        return warnings;
    }

    async Task ApplyFansCoreAsync(FanSettings fans, List<string> warnings)
    {
        try
        {
            if (fans.Mode == FanMode.Custom) fansDirty.Set();
            await hw.SetFansAsync(fans);
            ActiveFans = fans;
            if (fans.Mode != FanMode.Custom) fansDirty.Clear();
        }
        catch (Exception ex)
        {
            warnings.Add("Ventoinhas: " + ex.Message + " Voltando ao automático.");
            log?.Write("ERRO ventoinhas: " + ex.Message);
            try { await RevertFansToAutoAsync(); }
            catch (Exception ex2) { log?.Write("ERRO ao voltar ventoinhas: " + ex2.Message); }
        }
    }
}
