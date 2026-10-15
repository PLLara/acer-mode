using System.Diagnostics;
using System.Security.Principal;

namespace AcerMode.App.Startup;

public static class Elevation
{
    public static bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    // Relança o próprio exe pedindo UAC. Devolve false se a pessoa recusou.
    public static bool Relaunch(string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!, string.Join(' ', args.Select(a => '"' + a + '"')))
            {
                UseShellExecute = true,
                Verb = "runas",
            };
            Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}
