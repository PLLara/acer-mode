using AcerMode.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AcerMode.App.ViewModels;

// Temperaturas e ventoinhas ao vivo, mais o histórico de 2 minutos. Somente leitura.
public sealed partial class SystemViewModel : ObservableObject
{
    readonly AppState state;

    public SystemViewModel(AppState state)
    {
        this.state = state;
        state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.Latest) or nameof(AppState.SensorsAvailable)) Refresh();
        };
        Refresh();
    }

    public double? Cpu => state.Latest?.CpuTempC;
    public double? Gpu => state.Latest?.GpuTempC;
    public double? SystemTemp => state.Latest?.SystemTempC;
    public double? CpuRpm => state.Latest?.CpuFanRpm;
    public double? GpuRpm => state.Latest?.GpuFanRpm;
    public bool Available => state.SensorsAvailable;
    public bool Unavailable => !state.SensorsAvailable;

    [ObservableProperty] IReadOnlyList<double> cpuSeries = Array.Empty<double>();
    [ObservableProperty] IReadOnlyList<double> gpuSeries = Array.Empty<double>();

    void Refresh()
    {
        var h = state.Sensors.History.Snapshot();
        CpuSeries = h.Where(r => r.CpuTempC is not null).Select(r => (double)r.CpuTempC!).ToArray();
        GpuSeries = h.Where(r => r.GpuTempC is not null).Select(r => (double)r.GpuTempC!).ToArray();
        OnPropertyChanged(nameof(Cpu));
        OnPropertyChanged(nameof(Gpu));
        OnPropertyChanged(nameof(SystemTemp));
        OnPropertyChanged(nameof(CpuRpm));
        OnPropertyChanged(nameof(GpuRpm));
        OnPropertyChanged(nameof(Available));
        OnPropertyChanged(nameof(Unavailable));
    }
}
