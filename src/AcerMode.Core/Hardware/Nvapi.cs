using System.Runtime.InteropServices;

namespace AcerMode.Core;

// NV_GPU_PERF_PSTATES20_INFO_V2: a tabela que o NVIDIA App usa para o overclock. A API pública
// (NVML) devolve "não suportado" para offsets neste notebook (rc=3); o NVAPI responde e o P0
// é editável (núcleo −1000..+1000 MHz, memória −1000..+3000 MHz, lido em 2026-10-18).
//   cabeçalho: version, bIsEditable, numPstates, numClocks, numBaseVoltages  (5 × 4 bytes)
//   pstate (456 bytes): pstateId, bIsEditable, clocks[8] (44 bytes), baseVoltages[4] (24 bytes)
//   clock: domainId, typeId, bIsEditable, freqDelta_kHz {value, min, max}, dados (5 × 4)
public static class Pstates20
{
    public const int Size = 0x1CF8, PstateBase = 20, PstateSize = 456, ClockSize = 44;
    public const int GraphicsDomain = 0, MemoryDomain = 4;
    const int Version = Size | (2 << 16);

    public static int ClockOffset(int clock) => PstateBase + 8 + clock * ClockSize;

    public static byte[] BuildSet(int coreMhz, int memMhz)
    {
        var b = new byte[Size];
        Put(b, 0, Version);
        Put(b, 8, 1);    // só o P0
        Put(b, 12, 2);   // núcleo e memória
        Put(b, 16, 0);
        Put(b, PstateBase, 0);   // P0
        Put(b, ClockOffset(0), GraphicsDomain);
        Put(b, ClockOffset(0) + 12, coreMhz * 1000);
        Put(b, ClockOffset(1), MemoryDomain);
        Put(b, ClockOffset(1) + 12, memMhz * 1000);
        return b;
    }

    public static byte[] EmptyForGet()
    {
        var b = new byte[Size];
        Put(b, 0, Version);
        return b;
    }

    // Offsets do P0, procurados pelo domínio (a ordem dos clocks não é garantida).
    public static (int CoreMhz, int MemMhz) ParseOffsets(byte[] b)
    {
        int clocks = Math.Min(BitConverter.ToInt32(b, 12), 8);
        int core = 0, mem = 0;
        for (int c = 0; c < clocks; c++)
        {
            int at = ClockOffset(c);
            int domain = BitConverter.ToInt32(b, at), delta = BitConverter.ToInt32(b, at + 12) / 1000;
            if (domain == GraphicsDomain) core = delta;
            else if (domain == MemoryDomain) mem = delta;
        }
        return (core, mem);
    }

    static void Put(byte[] b, int at, int v) => BitConverter.GetBytes(v).CopyTo(b, at);
}

public sealed class NvapiGpuOverclock : IGpuOverclock
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface")] static extern IntPtr QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int EnumFn([Out] IntPtr[] gpus, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int PstatesFn(IntPtr gpu, IntPtr info);

    const uint Initialize = 0x0150E828, EnumPhysicalGPUs = 0xE5AC921F, GetPstates20 = 0x6FF81213, SetPstates20 = 0x0F4DAE6B;

    IntPtr gpu;
    PstatesFn? get, set;
    bool failed;

    static T? Fn<T>(uint id) where T : Delegate
    {
        var p = QueryInterface(id);
        return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    bool Ready()
    {
        if (get is not null && set is not null) return true;
        if (failed) return false;
        try
        {
            if (Fn<InitFn>(Initialize)?.Invoke() != 0) { failed = true; return false; }
            var gpus = new IntPtr[64];
            if (Fn<EnumFn>(EnumPhysicalGPUs)?.Invoke(gpus, out int n) != 0 || n < 1) { failed = true; return false; }
            gpu = gpus[0];
            get = Fn<PstatesFn>(GetPstates20);
            set = Fn<PstatesFn>(SetPstates20);
            failed = get is null || set is null;
            return !failed;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { failed = true; return false; }
    }

    static int Call(PstatesFn fn, IntPtr gpu, byte[] data)
    {
        IntPtr buf = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, buf, data.Length);
            int rc = fn(gpu, buf);
            Marshal.Copy(buf, data, 0, data.Length);
            return rc;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public (int CoreMhz, int MemMhz)? Read()
    {
        if (!Ready()) return null;
        var b = Pstates20.EmptyForGet();
        return Call(get!, gpu, b) == 0 ? Pstates20.ParseOffsets(b) : null;
    }

    public void Apply(int coreMhz, int memMhz)
    {
        if (!Ready()) throw new HardwareException("Driver da NVIDIA indisponível para overclock.");
        int rc = Call(set!, gpu, Pstates20.BuildSet(coreMhz, memMhz));
        if (rc != 0) throw new HardwareException($"O driver da NVIDIA recusou o overclock (código {rc}).");
    }
}

// Sem GPU NVIDIA (demonstração, capturas): guarda o valor em memória.
public sealed class FakeGpuOverclock : IGpuOverclock
{
    (int, int) current;
    public (int CoreMhz, int MemMhz)? Read() => current;
    public void Apply(int coreMhz, int memMhz) => current = (coreMhz, memMhz);
}
