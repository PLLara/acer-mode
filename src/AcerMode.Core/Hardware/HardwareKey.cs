using System.Management;

namespace AcerMode.Core;

// Botão físico de modo: a BIOS dispara APGeEvent com 07 05 01 a cada aperto,
// às vezes duas vezes seguidas; o debounce de 600 ms descarta a repetição.
public sealed class HardwareKey : IDisposable
{
    readonly AppLog log;
    ManagementEventWatcher? watcher;
    long lastPress = long.MinValue / 2;

    public HardwareKey(AppLog log) => this.log = log;
    public event Action? Pressed;

    public bool Start()
    {
        try
        {
            watcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"), new WqlEventQuery("SELECT * FROM APGeEvent"));
            watcher.EventArrived += (_, e) =>
            {
                var d = e.NewEvent["EventDetail"] as byte[];
                log.Write("APGeEvent " + (d is null ? "(vazio)" : BitConverter.ToString(d)));
                if (!Encoders.IsModeKeyEvent(d)) return;
                long now = Environment.TickCount64;
                if (now - lastPress < 600) { log.Write("  ignorado (repetido)"); return; }
                lastPress = now;
                Pressed?.Invoke();
            };
            watcher.Stopped += (_, e) => log.Write("watcher parou: " + e.Status);
            watcher.Start();
            log.Write("watcher iniciado");
            return true;
        }
        catch (Exception ex) { log.Write("watcher FALHOU: " + ex.Message); return false; }
    }

    public void Dispose()
    {
        try { watcher?.Stop(); } catch (ManagementException) { }
        watcher?.Dispose();
    }
}
