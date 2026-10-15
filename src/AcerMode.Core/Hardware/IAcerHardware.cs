namespace AcerMode.Core;

// Tudo o que o app pede ao notebook. AcerWmi fala com a BIOS; FakeHardware simula.
public interface IAcerHardware
{
    Task<int> GetModeCodeAsync();
    Task SetModeAsync(ThermalMode mode);
    Task ApplyLightingAsync(LightingProfile profile);
    Task<SensorReading> ReadSensorsAsync();
    Task<FanState> GetFansAsync();
    Task SetFansAsync(FanSettings fans);   // lança HardwareException se a leitura de volta divergir
}

public sealed class HardwareException(string message) : Exception(message);
