using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AcerMode.App.Controls;

public partial class TelemetryCard : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(TelemetryCard));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string), typeof(TelemetryCard));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double?), typeof(TelemetryCard),
        new PropertyMetadata(null, (d, _) => ((TelemetryCard)d).SyncA11y()));
    public static readonly DependencyProperty IconDataProperty = DependencyProperty.Register(nameof(IconData), typeof(Geometry), typeof(TelemetryCard));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(TelemetryCard));
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(nameof(Series), typeof(IReadOnlyList<double>), typeof(TelemetryCard));
    public static readonly DependencyProperty StaleProperty = DependencyProperty.Register(nameof(Stale), typeof(bool), typeof(TelemetryCard),
        new PropertyMetadata(false, (d, e) => ((TelemetryCard)d).StaleTag.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Unit { get => (string?)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public double? Value { get => (double?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Geometry? IconData { get => (Geometry?)GetValue(IconDataProperty); set => SetValue(IconDataProperty, value); }
    public Brush? Accent { get => (Brush?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public IReadOnlyList<double>? Series { get => (IReadOnlyList<double>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public bool Stale { get => (bool)GetValue(StaleProperty); set => SetValue(StaleProperty, value); }

    // Escala automática no gráfico (rpm); temperatura usa a faixa fixa 30–100 °C.
    public bool AutoScale { get => Chart.AutoScale; set => Chart.AutoScale = value; }

    public TelemetryCard() => InitializeComponent();

    void SyncA11y() =>
        System.Windows.Automation.AutomationProperties.SetName(this, Value is { } v ? $"{Title}: {v:N0} {Unit}" : $"{Title}: sem leitura");
}
