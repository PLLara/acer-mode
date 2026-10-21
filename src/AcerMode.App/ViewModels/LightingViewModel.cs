using System.Windows.Media;
using System.Windows.Threading;
using AcerMode.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AcerMode.App.ViewModels;

public sealed record EffectOption(KbEffect Effect, string Label);

// Editor de iluminação: perfil em edição, efeito, cores por zona, brilho, velocidade, direção.
// Mudanças gravam no perfil e, se for o perfil valendo agora, vão ao teclado com debounce.
public sealed partial class LightingViewModel : ObservableObject
{
    readonly AppState state;
    readonly DispatcherTimer liveTimer;
    readonly LatestWins<(LightingProfile Profile, ThermalMode? For)> live;
    bool loading, writing, followActive = true;

    public LightingViewModel(AppState state)
    {
        this.state = state;
        Effects = KbEffects.Supported.Select(e => new EffectOption(e, KbEffects.Label(e))).ToArray();
        editingMode = state.ActiveMode ?? ThermalMode.Balanced;

        // A prévia leva o modo do perfil editado; o ModeService a descarta se o botão físico
        // trocou o modo nesse meio-tempo.
        live = new LatestWins<(LightingProfile Profile, ThermalMode? For)>(x => state.Modes.PreviewLightingAsync(x.Profile, x.For));
        live.Error += ex => AppState.Ui(() => state.Banner = AppState.Friendly(ex));
        liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        liveTimer.Tick += (_, _) =>
        {
            liveTimer.Stop();
            if (IsLive) _ = live.PostAsync((Profile, state.Settings.IndependentLighting ? null : EditingMode));
        };

        state.ModeApplied += m =>
        {
            if (followActive && !state.Settings.IndependentLighting) EditingMode = m;
            OnPropertyChanged(nameof(LiveHint));
            SweepRequested?.Invoke();
        };
        state.ProfilesChanged += () => { if (!writing) Load(); };
        Load();
    }

    public IReadOnlyList<EffectOption> Effects { get; }
    public IReadOnlyList<ThermalMode> Modes { get; } = ThermalModes.All;

    static readonly Color[] swatches = new[]
    {
        // paleta do handoff de 2026-10-21
        "#FF4B1F", "#FFB51F", "#08E74B", "#0BB8EE", "#2374FF", "#9B48F9", "#ED309B", "#FFFFFF",
    }.Select(h => (Color)ColorConverter.ConvertFromString(h)).ToArray();

    public IReadOnlyList<Color> Swatches => swatches;

    public event Action? SweepRequested;

    [ObservableProperty] ThermalMode editingMode;
    [ObservableProperty] LightingProfile profile = new();
    [ObservableProperty] KbEffect effect;
    [ObservableProperty] int selectedZone = -1;
    [ObservableProperty] Color color = Colors.White;
    [ObservableProperty] string hex = "#FFFFFF";
    [ObservableProperty] string? hexError;
    [ObservableProperty] double brightness = 100;
    [ObservableProperty] double speed = 5;
    [ObservableProperty] bool leftToRight;

    public bool IndependentLighting
    {
        get => state.Settings.IndependentLighting;
        set
        {
            if (value == state.Settings.IndependentLighting) return;
            state.Settings.SetIndependentLighting(value, state.ActiveMode ?? EditingMode);
            state.SaveSoon();
            OnPropertyChanged();
            OnPropertyChanged(nameof(EditingProfiles));
            Load();
            ScheduleLive();
        }
    }

    public bool EditingProfiles => !state.Settings.IndependentLighting;
    public bool AllZones { get => SelectedZone < 0; set => SelectedZone = value ? -1 : 0; }
    public bool ShowsZones => KbEffects.UsesZones(Effect);
    public bool ShowsColor => KbEffects.UsesZones(Effect) || KbEffects.UsesColor(Effect);
    public bool ShowsSpeed => KbEffects.UsesSpeed(Effect);
    public bool ShowsDirection => KbEffects.UsesDirection(Effect);
    public bool IsLive => LightingPreviewPolicy.ShouldSendLive(EditingMode, state.ActiveMode, state.Settings.IndependentLighting);
    public string LiveHint => IsLive
        ? "Mudanças vão para o teclado na hora."
        : $"Editando {EditingMode}: aplica quando esse modo for ativado.";
    public string ZoneLabel => SelectedZone < 0 ? "Todas as zonas" : $"Zona {SelectedZone + 1}";

    LightingProfile Current => state.Settings.IndependentLighting && state.Settings.GlobalLighting is { } g
        ? g : state.Settings.Profile(EditingMode).Lighting;

    // ---- reações ----
    partial void OnEditingModeChanged(ThermalMode value)
    {
        followActive = value == state.ActiveMode;
        Load();
    }

    partial void OnEffectChanged(KbEffect value)
    {
        OnPropertyChanged(nameof(ShowsZones));
        OnPropertyChanged(nameof(ShowsColor));
        OnPropertyChanged(nameof(ShowsSpeed));
        OnPropertyChanged(nameof(ShowsDirection));
        if (loading) return;
        Update(p => p with { Effect = value });
        LoadColor();
    }

    partial void OnSelectedZoneChanged(int value)
    {
        OnPropertyChanged(nameof(AllZones));
        OnPropertyChanged(nameof(ZoneLabel));
        if (!loading) LoadColor();
    }

    partial void OnColorChanged(Color value)
    {
        var h = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        if (!string.Equals(Hex, h, StringComparison.OrdinalIgnoreCase)) { bool l = loading; loading = true; Hex = h; loading = l; }
        HexError = null;
        if (loading) return;
        var rgb = LightingBrushes.ToRgb(value);
        Update(p =>
        {
            if (p.Effect != KbEffect.Static) return p with { Color = rgb };
            var zones = (Rgb[])p.Zones.Clone();
            if (SelectedZone < 0) Array.Fill(zones, rgb); else zones[SelectedZone] = rgb;
            return p with { Zones = zones, Color = rgb };
        });
    }

    partial void OnHexChanged(string value)
    {
        if (loading) return;
        if (!Rgb.TryFromHex(value, out var rgb)) { HexError = "Use o formato #RRGGBB"; return; }
        HexError = null;
        var c = LightingBrushes.ToColor(rgb);
        if (c != Color) Color = c;
    }

    partial void OnBrightnessChanged(double value) { if (!loading) Update(p => p with { Brightness = (int)Math.Round(value) }); }
    partial void OnSpeedChanged(double value) { if (!loading) Update(p => p with { Speed = (int)Math.Round(value) }); }
    partial void OnLeftToRightChanged(bool value) { if (!loading) Update(p => p with { Direction = value ? KbDirection.LeftToRight : KbDirection.RightToLeft }); }

    [RelayCommand]
    void PickSwatch(Color c) => Color = c;

    // ---- leitura e escrita do perfil ----
    void Load()
    {
        loading = true;
        var p = Current.Normalized();
        Profile = p;
        Effect = p.Effect;
        Brightness = p.Brightness;
        Speed = p.Speed;
        LeftToRight = p.Direction == KbDirection.LeftToRight;
        loading = false;
        LoadColor();
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(LiveHint));
        OnPropertyChanged(nameof(IndependentLighting));
        OnPropertyChanged(nameof(EditingProfiles));
    }

    void LoadColor()
    {
        bool l = loading;
        loading = true;
        var p = Current.Normalized();
        var rgb = p.Effect == KbEffect.Static ? p.Zones[Math.Max(0, SelectedZone)] : p.Color;
        Color = LightingBrushes.ToColor(rgb);
        loading = l;
    }

    void Update(Func<LightingProfile, LightingProfile> change)
    {
        var next = change(Current).Normalized();
        if (state.Settings.IndependentLighting) state.Settings.GlobalLighting = next;
        else state.Settings.Profile(EditingMode).Lighting = next;
        Profile = next;
        state.SaveSoon();
        writing = true;
        state.NotifyProfilesChanged();
        writing = false;
        ScheduleLive();
    }

    void ScheduleLive()
    {
        liveTimer.Stop();
        liveTimer.Start();
    }
}
