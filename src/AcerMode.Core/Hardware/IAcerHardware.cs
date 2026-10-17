namespace AcerMode.Core;

// Tudo o que o app pede ao notebook. AcerWmi fala com a BIOS; FakeHardware simula.
public interface IAcerHardware
{
    Task<int> GetModeCodeAsync();
    Task SetModeAsync(ThermalMode mode);
    Task ApplyLightingAsync(LightingProfile profile);
    Task<SensorReading> ReadSensorsAsync();
    Task<int?> ReadCpuTempAsync();          // 1 chamada à BIOS; reserva quando o Core Temp não está aberto
    Task<FanState> GetFansAsync();
    Task SetFansAsync(FanSettings fans);   // lança HardwareException se a leitura de volta divergir
    Task SetFanSpeedsAsync(int cpuPercent, int gpuPercent);   // só o %, com as ventoinhas já em Personalizado (2 chamadas)
}

public sealed class HardwareException(string message) : Exception(message);
