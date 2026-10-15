using System.IO;
using AcerMode.Core;

namespace AcerMode.App.Startup;

// Comandos sem janela. Devolve o código de saída, ou null se não é um comando de CLI.
public static class Cli
{
    public static async Task<int?> TryRunAsync(string[] args)
    {
        if (args.Length == 0) return null;
        switch (args[0])
        {
            case "--set":
            case "--status":
                if (!Elevation.IsAdmin) { Elevation.Relaunch(args); return 0; }
                return await SetOrStatusAsync(args);
            case "--probe":
                if (!Elevation.IsAdmin) { Elevation.Relaunch(args); return 0; }
                return await ProbeAsync();
            case "--shot":
            case "--shot-osd":
                return await Shots.RunAsync(args);
            default:
                return null;
        }
    }

    // AcerMode.exe --set turbo | --status → grava o resultado em last-status.txt ao lado do exe.
    static async Task<int> SetOrStatusAsync(string[] args)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "last-status.txt");
        using var hw = new AcerWmi();
        try
        {
            if (args[0] == "--set")
            {
                if (args.Length < 2 || !ThermalModes.TryParse(args[1], out var mode)) { File.WriteAllText(file, "ERR modo inválido"); return 2; }
                var dir = SettingsStore.DefaultDirectory;
                var (settings, _) = new SettingsStore(Path.Combine(dir, "settings.json")).Load();
                var svc = new ModeService(hw, () => settings, new DirtyMarker(Path.Combine(dir, "fans-dirty")));
                await svc.ApplyModeAsync(mode);
            }
            int cur = await hw.GetModeCodeAsync();
            File.WriteAllText(file, "Profile: " + (ThermalModes.IsKnown(cur) ? ((ThermalMode)cur).ToString() : "desconhecido (" + cur + ")"));
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(file, "ERR " + ex.Message); return 1; }
    }

    static async Task<int> ProbeAsync()
    {
        var dir = SettingsStore.DefaultDirectory;
        Directory.CreateDirectory(dir);
        using var hw = new AcerWmi();
        var lines = await hw.ProbeAsync();
        File.WriteAllLines(Path.Combine(dir, $"probe-{DateTime.Now:yyyyMMdd-HHmmss}.txt"), lines);
        return 0;
    }
}
