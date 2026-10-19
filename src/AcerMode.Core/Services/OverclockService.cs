namespace AcerMode.Core;

// Overclock com rede de segurança. O offset só vale com o app aberto e volta a 0 quando:
//   • a GPU passa de 85 °C;
//   • o driver de vídeo reinicia (evento 4101 do Windows, "o driver parou de responder");
//   • a sessão anterior terminou com o OC ligado sem fechar direito (tela azul, travamento):
//     nesse caso o app não reaplica e desliga o OC nas configurações.
// O marcador em disco é o que permite detectar o último caso.
public sealed class OverclockService
{
    public const int GpuLimitC = 85;

    readonly IGpuOverclock oc;
    readonly DirtyMarker marker;
    readonly AppLog? log;

    public OverclockService(IGpuOverclock oc, DirtyMarker marker, AppLog? log = null) { this.oc = oc; this.marker = marker; this.log = log; }

    public bool Active { get; private set; }
    public (int CoreMhz, int MemMhz) Applied { get; private set; }

    public (int CoreMhz, int MemMhz)? ReadDriver() => oc.Read();

    public OcStartupResult Startup(GpuOcSettings settings)
    {
        if (marker.Exists)
        {
            TryZero();
            marker.Clear();
            log?.Write("overclock: a última sessão terminou com OC ligado sem fechar direito; OC desligado");
            return new OcStartupResult(true,
                "A última sessão terminou com o overclock ligado sem fechar direito (tela azul ou travamento). O overclock foi desligado.");
        }
        if (settings.Enabled) Apply(settings);
        return new OcStartupResult(false, null);
    }

    public void Apply(GpuOcSettings settings)
    {
        var s = settings.Normalized();
        if (!s.Enabled || (s.CoreMhz == 0 && s.MemMhz == 0)) { Revert("desligado"); return; }

        marker.Set();   // antes de escrever: se travar no meio, a próxima partida sabe
        try
        {
            oc.Apply(s.CoreMhz, s.MemMhz);
            var back = oc.Read();
            if (back != (s.CoreMhz, s.MemMhz))
                throw new HardwareException($"O driver não confirmou o overclock (leu núcleo {back?.CoreMhz} / memória {back?.MemMhz}).");
        }
        catch (HardwareException)
        {
            TryZero();
            marker.Clear();
            Active = false;
            throw;
        }
        Active = true;
        Applied = (s.CoreMhz, s.MemMhz);
        log?.Write($"overclock aplicado: núcleo +{s.CoreMhz} MHz, memória +{s.MemMhz} MHz");
    }

    // Devolve a mensagem quando reverteu; null quando não havia o que fazer.
    public string? OnReading(SensorReading r)
    {
        if (!Active || r.GpuTempC is not { } gpu || gpu <= GpuLimitC) return null;
        Revert($"GPU a {gpu} °C");
        return $"Overclock desligado às {DateTime.Now:HH:mm}: a GPU passou de {GpuLimitC} °C ({gpu} °C).";
    }

    public string? OnDriverReset()
    {
        if (!Active) return null;
        Revert("o driver de vídeo reiniciou");
        return $"Overclock desligado às {DateTime.Now:HH:mm}: o driver de vídeo parou de responder e reiniciou.";
    }

    // OC que não é do app (NVIDIA App, PredatorSense): offsets diferentes de zero com o app desligado.
    public (int CoreMhz, int MemMhz)? ExternalOffsets()
    {
        if (Active) return null;
        return oc.Read() is { } d && (d.CoreMhz != 0 || d.MemMhz != 0) ? d : null;
    }

    public void ZeroExternal() { TryZero(); log?.Write("overclock externo zerado"); }

    public void RevertOnExit() { if (Active) Revert("app fechado"); }

    void Revert(string why)
    {
        bool was = Active;
        TryZero();
        marker.Clear();
        Active = false;
        Applied = (0, 0);
        if (was) log?.Write("overclock revertido: " + why);
    }

    void TryZero()
    {
        try { oc.Apply(0, 0); }
        catch (HardwareException ex) { log?.Write("ERRO ao zerar overclock: " + ex.Message); }
    }
}
