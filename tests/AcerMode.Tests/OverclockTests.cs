using AcerMode.Core;
using Xunit;

public class OverclockTests
{
    // ---- limites ----
    [Fact] public void Settings_ClampToSafeRange() => Assert.Equal(new GpuOcSettings(true, 150, 500), new GpuOcSettings(true, 999, 9999).Normalized());
    [Fact] public void Settings_NeverNegative() => Assert.Equal(new GpuOcSettings(true, 0, 0), new GpuOcSettings(true, -300, -50).Normalized());
    [Fact] public void Settings_SnapToSteps() => Assert.Equal(new GpuOcSettings(true, 75, 250), new GpuOcSettings(true, 80, 260).Normalized());
    [Fact] public void Defaults_AreOff() => Assert.False(AppSettings.Defaults().GpuOc.Enabled);

    // ---- tabela P-state 2.0 do NVAPI ----
    [Fact]
    public void BuildSet_WritesP0DeltasForCoreAndMemory()
    {
        var b = Pstates20.BuildSet(coreMhz: 90, memMhz: 300);
        Assert.Equal(Pstates20.Size | (2 << 16), BitConverter.ToInt32(b, 0));   // versão 2
        Assert.Equal(1, BitConverter.ToInt32(b, 8));                            // 1 P-state
        Assert.Equal(2, BitConverter.ToInt32(b, 12));                           // 2 clocks
        Assert.Equal(0, BitConverter.ToInt32(b, 16));                           // 0 tensões
        Assert.Equal(0, BitConverter.ToInt32(b, Pstates20.PstateBase));         // P0
        Assert.Equal(Pstates20.GraphicsDomain, BitConverter.ToInt32(b, Pstates20.ClockOffset(0)));
        Assert.Equal(90_000, BitConverter.ToInt32(b, Pstates20.ClockOffset(0) + 12));
        Assert.Equal(Pstates20.MemoryDomain, BitConverter.ToInt32(b, Pstates20.ClockOffset(1)));
        Assert.Equal(300_000, BitConverter.ToInt32(b, Pstates20.ClockOffset(1) + 12));
    }

    [Fact]
    public void ParseOffsets_ReadsP0ByDomain()
    {
        // leitura real tem a memória primeiro às vezes: procura pelo domínio, não pela posição
        var b = new byte[Pstates20.Size];
        BitConverter.GetBytes(Pstates20.Size | (2 << 16)).CopyTo(b, 0);
        BitConverter.GetBytes(1).CopyTo(b, 8);
        BitConverter.GetBytes(2).CopyTo(b, 12);
        BitConverter.GetBytes(Pstates20.MemoryDomain).CopyTo(b, Pstates20.ClockOffset(0));
        BitConverter.GetBytes(500_000).CopyTo(b, Pstates20.ClockOffset(0) + 12);
        BitConverter.GetBytes(Pstates20.GraphicsDomain).CopyTo(b, Pstates20.ClockOffset(1));
        BitConverter.GetBytes(-15_000).CopyTo(b, Pstates20.ClockOffset(1) + 12);
        Assert.Equal((-15, 500), Pstates20.ParseOffsets(b));
    }

    // ---- serviço com a rede de segurança ----
    sealed class FakeOc : IGpuOverclock
    {
        public (int Core, int Mem) Current;
        public List<string> Calls = new();
        public bool LieOnReadBack;
        public (int CoreMhz, int MemMhz)? Read() => LieOnReadBack ? (0, 0) : Current;
        public void Apply(int coreMhz, int memMhz) { Calls.Add($"{coreMhz}/{memMhz}"); Current = (coreMhz, memMhz); }
    }

    readonly FakeOc oc = new();
    readonly DirtyMarker marker = new(Path.Combine(Path.GetTempPath(), "acermode-oc-" + Guid.NewGuid().ToString("N")));
    OverclockService Svc() => new(oc, marker);

    [Fact]
    public void Apply_SetsOffsetsAndMarker()
    {
        var svc = Svc();
        svc.Apply(new GpuOcSettings(true, 90, 300));
        Assert.Equal((90, 300), oc.Current);
        Assert.True(svc.Active);
        Assert.True(marker.Exists);
    }

    [Fact]
    public void Apply_Disabled_RevertsToZero()
    {
        var svc = Svc();
        svc.Apply(new GpuOcSettings(true, 90, 300));
        svc.Apply(new GpuOcSettings(false, 90, 300));
        Assert.Equal((0, 0), oc.Current);
        Assert.False(svc.Active);
        Assert.False(marker.Exists);
    }

    [Fact]
    public void Apply_ReadBackMismatch_RevertsAndThrows()
    {
        oc.LieOnReadBack = true;
        var svc = Svc();
        Assert.Throws<HardwareException>(() => svc.Apply(new GpuOcSettings(true, 90, 300)));
        Assert.Equal((0, 0), oc.Current);
        Assert.False(marker.Exists);
    }

    [Fact]
    public void Startup_AfterUncleanSession_DoesNotReapply()
    {
        marker.Set();   // a sessão anterior terminou com OC ligado sem fechar direito
        var r = Svc().Startup(new GpuOcSettings(true, 90, 300));
        Assert.True(r.DisabledBySafety);
        Assert.NotNull(r.Message);
        Assert.Equal(new[] { "0/0" }, oc.Calls);
        Assert.False(marker.Exists);
    }

    [Fact]
    public void Startup_Clean_ReappliesSavedOc()
    {
        var r = Svc().Startup(new GpuOcSettings(true, 90, 300));
        Assert.False(r.DisabledBySafety);
        Assert.Equal((90, 300), oc.Current);
    }

    [Fact]
    public void HotGpu_RevertsOnce()
    {
        var svc = Svc();
        svc.Apply(new GpuOcSettings(true, 90, 300));
        Assert.Null(svc.OnReading(new SensorReading(70, 85, null, null, null, DateTime.Now)));   // 85 ainda ok
        var msg = svc.OnReading(new SensorReading(70, 86, null, null, null, DateTime.Now));
        Assert.NotNull(msg);
        Assert.Equal((0, 0), oc.Current);
        Assert.Null(svc.OnReading(new SensorReading(70, 90, null, null, null, DateTime.Now)));   // já revertido
    }

    [Fact]
    public void DriverReset_Reverts()
    {
        var svc = Svc();
        svc.Apply(new GpuOcSettings(true, 90, 300));
        Assert.NotNull(svc.OnDriverReset());
        Assert.Equal((0, 0), oc.Current);
        Assert.Null(svc.OnDriverReset());
    }

    [Fact]
    public void External_IsDetectedWhenAppIsOff()
    {
        oc.Current = (120, 800);   // NVIDIA App ligou OC
        var svc = Svc();
        Assert.Equal((120, 800), svc.ExternalOffsets());
        svc.Apply(new GpuOcSettings(true, 90, 300));
        Assert.Null(svc.ExternalOffsets());   // agora quem manda é o app
    }

    [Fact]
    public void Exit_RevertsAndClearsMarker()
    {
        var svc = Svc();
        svc.Apply(new GpuOcSettings(true, 90, 300));
        svc.RevertOnExit();
        Assert.Equal((0, 0), oc.Current);
        Assert.False(marker.Exists);
    }
}
