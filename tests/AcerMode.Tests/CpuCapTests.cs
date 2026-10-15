using AcerMode.Core;
using Xunit;

public class CpuCapTests
{
    sealed class FakePower : IPowerConfig
    {
        public Dictionary<string, int> Ac = new();
        public int Activations;
        public List<string> Sets = new();
        public int? GetAcValue(string guid) => Ac.TryGetValue(guid, out var v) ? v : null;
        public void SetAcValue(string guid, int value) { Ac[guid] = value; Sets.Add(guid + "=" + value); }
        public void ActivateCurrent() => Activations++;
    }

    const string PtOutput = """
        GUID do Esquema de Energia: 733787f0-39fa-4195-a6c7-6936ed6deff8  (Acer)
          GUID de Subgrupos: 54533251-82be-4824-96c1-47b60b740d00  (Gerenciamento de energia do processador)
            GUID de Configuração de Energia: 75b0ae3f-bce0-45a7-8c89-c9611c25e100  (Frequência máxima do processador)
              Unidades de Configurações Possíveis: MHz
            Índice de Configurações de Correntes Alternadas Atuais: 0x00001068
            Índice de Configurações de Correntes Contínuas Atuais: 0x00000000
        """;

    const string EnOutput = """
            Current AC Power Setting Index: 0x00000ed8
            Current DC Power Setting Index: 0x00000000
        """;

    [Fact] public void Parse_Portuguese() => Assert.Equal(4200, PowerCfgParser.ParseAcIndex(PtOutput));
    [Fact] public void Parse_English() => Assert.Equal(3800, PowerCfgParser.ParseAcIndex(EnOutput));
    [Fact] public void Parse_Garbage_IsNull() => Assert.Null(PowerCfgParser.ParseAcIndex("Acesso negado."));

    [Fact]
    public void Enforce_SetsBothCoreClassesAndActivates_WhenRemoved()
    {
        var pc = new FakePower();
        var changed = new CpuCapService(pc).Enforce(new CpuCapSettings(true, 4200));
        Assert.True(changed);
        Assert.Equal(4200, pc.GetAcValue(CpuCapService.PerformanceCores));
        Assert.Equal(4200, pc.GetAcValue(CpuCapService.EfficiencyCores));
        Assert.Equal(1, pc.Activations);
    }

    [Fact]
    public void Enforce_IsNoOp_WhenAlreadyApplied()
    {
        var pc = new FakePower { Ac = { [CpuCapService.PerformanceCores] = 4200, [CpuCapService.EfficiencyCores] = 4200 } };
        Assert.False(new CpuCapService(pc).Enforce(new CpuCapSettings(true, 4200)));
        Assert.Empty(pc.Sets);
        Assert.Equal(0, pc.Activations);
    }

    [Fact]
    public void Disabled_RemovesTheCap()
    {
        var pc = new FakePower { Ac = { [CpuCapService.PerformanceCores] = 4200, [CpuCapService.EfficiencyCores] = 4200 } };
        new CpuCapService(pc).Enforce(new CpuCapSettings(false, 4200));
        Assert.Equal(0, pc.GetAcValue(CpuCapService.PerformanceCores));   // 0 = sem limite
        Assert.Equal(0, pc.GetAcValue(CpuCapService.EfficiencyCores));
    }

    [Fact]
    public void Settings_AreClampedToSaneRange()
    {
        Assert.Equal(CpuCapSettings.MinMhz, new CpuCapSettings(true, 100).Normalized().Mhz);
        Assert.Equal(CpuCapSettings.MaxMhz, new CpuCapSettings(true, 99999).Normalized().Mhz);
        Assert.Equal(4200, new CpuCapSettings(true, 4249).Normalized().Mhz);   // passos de 100 MHz
    }

    [Fact]
    public void Defaults_KeepTheOldWatchdogBehaviour()
    {
        var s = AppSettings.Defaults();
        Assert.True(s.CpuCap.Enabled);
        Assert.Equal(4200, s.CpuCap.Mhz);
    }
}
