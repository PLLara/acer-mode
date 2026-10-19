using System.Diagnostics.Eventing.Reader;

namespace AcerMode.App.Startup;

// Avisa quando o Windows registra "o driver de vídeo parou de responder e se recuperou"
// (log Sistema, origem Display, evento 4101): sinal clássico de overclock instável.
public sealed class DriverResetWatcher : IDisposable
{
    EventLogWatcher? watcher;
    public event Action? DriverReset;

    public bool Start()
    {
        try
        {
            var q = new EventLogQuery("System", PathType.LogName, "*[System[Provider[@Name='Display'] and (EventID=4101)]]");
            watcher = new EventLogWatcher(q);
            watcher.EventRecordWritten += (_, e) => { if (e.EventRecord is not null) DriverReset?.Invoke(); };
            watcher.Enabled = true;
            return true;
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        if (watcher is null) return;
        try { watcher.Enabled = false; } catch (EventLogException) { }
        watcher.Dispose();
    }
}
