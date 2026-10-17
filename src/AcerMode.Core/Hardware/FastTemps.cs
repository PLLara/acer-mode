using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace AcerMode.Core;

// Temperaturas baratas para o polling em segundo plano. Ler sensores pela BIOS
// (GetGamingSysInfo) leva ~15 ms e congela os núcleos por até ~19 ms: com 5 leituras a cada
// 2 s isso dava stutter nos jogos (medido em 2026-10-17). Estas fontes não tocam a BIOS:
//   GPU: driver NVIDIA (NVML), ~0,7 ms, sem paradas medidas.
//   CPU: memória compartilhada do Core Temp (que lê os MSRs pelo próprio driver), microssegundos.
public interface IFastTemps
{
    int? CpuC();
    int? GpuC();
}

// CORE_TEMP_SHARED_DATA do Core Temp (https://www.alcpu.com/CoreTemp/developers.html).
public static class CoreTempLayout
{
    public const int TjMax = 1024, CoreCount = 1536, CpuCount = 1540, Temps = 1544;
    public const int Fahrenheit = 2684, DeltaToTjMax = 2685, Size = 2686;

    public static int? HottestC(ReadOnlySpan<byte> b)
    {
        if (b.Length < Size) return null;
        int cores = (int)Math.Min(BitConverter.ToUInt32(b[CoreCount..]), 256u);
        if (cores == 0) return null;
        bool fahrenheit = b[Fahrenheit] != 0, delta = b[DeltaToTjMax] != 0;
        float tj = BitConverter.ToUInt32(b[TjMax..]);
        float hottest = float.MinValue;
        for (int i = 0; i < cores; i++)
        {
            float v = BitConverter.ToSingle(b[(Temps + 4 * i)..]);
            if (delta) v = tj - v;
            if (fahrenheit) v = (v - 32) * 5 / 9;
            if (v > hottest) hottest = v;
        }
        return SensorReading.Plausible(Sensor.CpuTemp, (int)Math.Round(hottest));
    }
}

public sealed class CoreTempCpu
{
    readonly byte[] buffer = new byte[CoreTempLayout.Size];

    // Abre a cada leitura: o Core Temp pode ser fechado/aberto a qualquer momento.
    public int? Read()
    {
        try
        {
            using var mm = MemoryMappedFile.OpenExisting("CoreTempMappingObject", MemoryMappedFileRights.Read);
            using var view = mm.CreateViewAccessor(0, CoreTempLayout.Size, MemoryMappedFileAccess.Read);
            view.ReadArray(0, buffer, 0, buffer.Length);
            return CoreTempLayout.HottestC(buffer);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException) { return null; }
    }
}

public sealed class NvmlGpu
{
    [DllImport("nvml.dll")] static extern int nvmlInit_v2();
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temp);

    IntPtr device;
    bool ready, failed;

    public int? Read()
    {
        if (failed) return null;
        try
        {
            if (!ready)
            {
                if (nvmlInit_v2() != 0 || nvmlDeviceGetHandleByIndex_v2(0, out device) != 0) { failed = true; return null; }
                ready = true;
            }
            return nvmlDeviceGetTemperature(device, 0, out var t) == 0 ? SensorReading.Plausible(Sensor.GpuTemp, (int)t) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { failed = true; return null; }
    }
}

public sealed class FastTemps : IFastTemps
{
    readonly CoreTempCpu cpu = new();
    readonly NvmlGpu gpu = new();
    public int? CpuC() => cpu.Read();
    public int? GpuC() => gpu.Read();
}
