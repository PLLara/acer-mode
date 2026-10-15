using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using AcerMode.App.Theme;
using AcerMode.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AcerMode.App.ViewModels;

public sealed partial class ModeCardVm : ObservableObject
{
    public ModeCardVm(ThermalMode mode) => Mode = mode;

    public ThermalMode Mode { get; }
    public string Name => Mode.ToString();
    public string Description => ThermalModes.Description(Mode);
    public Geometry? Icon => Application.Current?.TryFindResource("Icon." + Mode) as Geometry;
    public Color Accent => AccentManager.For(Mode);

    [ObservableProperty] bool isActive;
    [ObservableProperty] Brush strip = Brushes.Transparent;
}

public sealed partial class ModesViewModel : ObservableObject
{
    readonly AppState state;

    public ModesViewModel(AppState state)
    {
        this.state = state;
        Cards = new ObservableCollection<ModeCardVm>(ThermalModes.All.Select(m => new ModeCardVm(m)));
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppState.ActiveMode)) Refresh();
            if (e.PropertyName == nameof(AppState.Latest)) OnPropertyChanged(nameof(Telemetry));
        };
        state.ProfilesChanged += RefreshStrips;
        Refresh();
        RefreshStrips();
    }

    public AppState State => state;
    public ObservableCollection<ModeCardVm> Cards { get; }

    public string ActiveName => state.ActiveMode?.ToString() ?? "Desconhecido";
    public string ActiveDescription => state.ActiveMode is { } m ? ThermalModes.Description(m) : "A BIOS informou um modo que o app não conhece.";
    public Geometry? ActiveIcon => state.ActiveMode is { } m ? Application.Current?.TryFindResource("Icon." + m) as Geometry : null;
    public SensorReading? Telemetry => state.Latest;

    // Cliques durante uma troca em andamento não podem ser ignorados: LatestWins coalesce.
    [RelayCommand(AllowConcurrentExecutions = true)]
    Task Select(ThermalMode mode) => state.SelectModeAsync(mode);

    void Refresh()
    {
        foreach (var c in Cards) c.IsActive = c.Mode == state.ActiveMode;
        OnPropertyChanged(nameof(ActiveName));
        OnPropertyChanged(nameof(ActiveDescription));
        OnPropertyChanged(nameof(ActiveIcon));
    }

    void RefreshStrips()
    {
        foreach (var c in Cards) c.Strip = LightingBrushes.Strip(state.Settings.LightingFor(c.Mode));
    }
}

// Faixa com as cores de um perfil de iluminação (4 zonas, ou a cor do efeito).
public static class LightingBrushes
{
    public static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);
    public static Rgb ToRgb(Color c) => new(c.R, c.G, c.B);

    public static Brush Strip(LightingProfile p)
    {
        var n = p.Normalized();
        Brush b;
        if (n.Effect == KbEffect.Static)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            for (int i = 0; i < 4; i++)
            {
                var c = ToColor(n.Zones[i]);
                g.GradientStops.Add(new GradientStop(c, i / 4.0));
                g.GradientStops.Add(new GradientStop(c, (i + 1) / 4.0));
            }
            b = g;
        }
        else if (KbEffects.UsesColor(n.Effect)) b = new SolidColorBrush(ToColor(n.Color));
        else
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            foreach (var (c, o) in new[] { ("#FF3B3B", 0.0), ("#FFB020", 0.25), ("#22C55E", 0.5), ("#22D3EE", 0.75), ("#8B5CF6", 1.0) })
                g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c), o));
            b = g;
        }
        b.Freeze();
        return b;
    }
}
