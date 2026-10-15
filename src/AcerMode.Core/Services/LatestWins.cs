namespace AcerMode.Core;

// Executa a ação com o valor mais recente: enquanto uma execução está em curso, só o
// último valor postado fica esperando. Evita encher a BIOS de escritas (roda de cor, cliques).
public sealed class LatestWins<T>
{
    readonly Func<T, Task> run;
    readonly object gate = new();
    bool running, hasPending;
    T pending = default!;

    public LatestWins(Func<T, Task> run) => this.run = run;

    public event Action<Exception>? Error;

    public async Task PostAsync(T value)
    {
        lock (gate)
        {
            if (running) { pending = value; hasPending = true; return; }
            running = true;
        }
        var current = value;
        while (true)
        {
            try { await run(current); }
            catch (Exception ex) { Error?.Invoke(ex); }
            lock (gate)
            {
                if (!hasPending) { running = false; return; }
                current = pending;
                hasPending = false;
            }
        }
    }
}
