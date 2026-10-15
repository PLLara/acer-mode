namespace AcerMode.App.Startup;

// Abrir de novo só traz a janela da instância que já está na bandeja.
public sealed class SingleInstance : IDisposable
{
    readonly Mutex mutex;
    readonly EventWaitHandle showEvent;
    public bool IsFirst { get; }

    public SingleInstance(string name)
    {
        mutex = new Mutex(true, @"Local\" + name, out bool created);
        IsFirst = created;
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + name + "Show");
    }

    public void SignalFirst() => showEvent.Set();

    public void Listen(Action onShow) =>
        new Thread(() => { while (true) { showEvent.WaitOne(); onShow(); } }) { IsBackground = true, Name = "SingleInstance" }.Start();

    public void Dispose()
    {
        if (IsFirst) { try { mutex.ReleaseMutex(); } catch (ApplicationException) { } }
        mutex.Dispose();
        showEvent.Dispose();
    }
}
