using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AcerMode.Core;

// Teto de frequência da CPU pelo plano de energia do Windows (na tomada).
// Portado de C:\Users\pedro\Tools\cpu-freq-cap-watchdog.ps1 em 2026-10-15: mesmo alvo
// (4200 MHz), mesmas duas configurações (núcleos P e E), mesma reaplicação periódica.
public sealed record CpuCapSettings(bool Enabled = true, int Mhz = 4200)
{
    public const int MinMhz = 1500, MaxMhz = 5800, Step = 100;

    public CpuCapSettings Normalized() =>
        this with { Mhz = Math.Clamp((int)Math.Round(Mhz / (double)Step) * Step, MinMhz, MaxMhz) };
}

public interface IPowerConfig
{
    int? GetAcValue(string settingGuid);
    void SetAcValue(string settingGuid, int value);
    void ActivateCurrent();
}

public static class PowerCfgParser
{
    // "Índice de Configurações de Correntes Alternadas Atuais: 0x00001068" ou
    // "Current AC Power Setting Index: 0x00001068".
    static readonly Regex AcLine = new(@"(Alternad|Current AC)[^\r\n]*?0x([0-9a-fA-F]+)", RegexOptions.IgnoreCase);

    public static int? ParseAcIndex(string output)
    {
        var m = AcLine.Match(output);
        return m.Success ? Convert.ToInt32(m.Groups[2].Value, 16) : null;
    }
}

// powercfg.exe de verdade, no plano ativo.
public sealed class PowerCfg : IPowerConfig
{
    const string Sub = "SUB_PROCESSOR";

    static (int Code, string Output) Run(string args)
    {
        var psi = new ProcessStartInfo("powercfg.exe", args)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    public int? GetAcValue(string settingGuid)
    {
        var (code, output) = Run($"/query SCHEME_CURRENT {Sub} {settingGuid}");
        return code == 0 ? PowerCfgParser.ParseAcIndex(output) : null;
    }

    public void SetAcValue(string settingGuid, int value)
    {
        var (code, output) = Run($"/setacvalueindex SCHEME_CURRENT {Sub} {settingGuid} {value}");
        if (code != 0) throw new InvalidOperationException("powercfg recusou o teto da CPU: " + output.Trim());
    }

    public void ActivateCurrent() => Run("/setactive SCHEME_CURRENT");
}

// Plano de energia em memória (demonstração e capturas não mexem no Windows).
public sealed class FakePowerConfig : IPowerConfig
{
    readonly Dictionary<string, int> values = new();
    public int? GetAcValue(string settingGuid) => values.TryGetValue(settingGuid, out var v) ? v : null;
    public void SetAcValue(string settingGuid, int value) => values[settingGuid] = value;
    public void ActivateCurrent() { }
}

public sealed class CpuCapService
{
    // Frequência máxima do processador (núcleos P) e da classe de eficiência 1 (núcleos E).
    public const string PerformanceCores = "75b0ae3f-bce0-45a7-8c89-c9611c25e100";
    public const string EfficiencyCores = "75b0ae3f-bce0-45a7-8c89-c9611c25e101";

    readonly IPowerConfig power;
    readonly AppLog? log;

    public CpuCapService(IPowerConfig power, AppLog? log = null) { this.power = power; this.log = log; }

    // Reaplica o teto se alguém (Windows, driver da Acer, outro app) o tirou.
    // Desligado = 0, que é "sem limite" para o Windows. Devolve true se mudou algo.
    public bool Enforce(CpuCapSettings settings)
    {
        var s = settings.Normalized();
        int target = s.Enabled ? s.Mhz : 0;
        bool changed = false;
        foreach (var guid in new[] { PerformanceCores, EfficiencyCores })
        {
            if (power.GetAcValue(guid) == target) continue;
            power.SetAcValue(guid, target);
            changed = true;
        }
        if (changed)
        {
            power.ActivateCurrent();
            log?.Write(s.Enabled ? $"teto da CPU reaplicado: {target} MHz" : "teto da CPU removido");
        }
        return changed;
    }

    public int? CurrentMhz() => power.GetAcValue(PerformanceCores);
}
