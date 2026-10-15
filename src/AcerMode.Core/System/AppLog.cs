namespace AcerMode.Core;

// Log de eventos em %APPDATA%\AcerMode\events.log, com um arquivo de rodízio (.1.log).
public sealed class AppLog
{
    readonly long maxBytes;
    readonly object gate = new();
    public string Path { get; }

    public AppLog(string path, long maxBytes = 512 * 1024) { Path = path; this.maxBytes = maxBytes; }

    public void Write(string message)
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length > maxBytes)
                    File.Move(Path, System.IO.Path.ChangeExtension(Path, null) + ".1.log", overwrite: true);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}\r\n");
            }
            catch (IOException) { }   // log nunca derruba o app
        }
    }

    public IReadOnlyList<string> Tail(int n)
    {
        lock (gate)
        {
            if (!File.Exists(Path)) return Array.Empty<string>();
            var lines = File.ReadAllLines(Path);
            return lines.Skip(Math.Max(0, lines.Length - n)).ToArray();
        }
    }
}
