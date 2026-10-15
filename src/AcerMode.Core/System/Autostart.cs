using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace AcerMode.Core;

// Iniciar com o Windows por tarefa agendada (roda elevado sem UAC no logon).
public static class Autostart
{
    const string TaskName = "AcerMode";

    static int Run(string args, out string output)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    public static bool IsOn() => Run("/Query /TN " + TaskName, out _) == 0;

    public static void Set(bool on, string exePath)
    {
        if (!on) { Run("/Delete /TN " + TaskName + " /F", out _); return; }
        string user = WindowsIdentity.GetCurrent().Name;
        // XML próprio: o padrão do schtasks não inicia na bateria, e isto é um notebook.
        string xml =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
            "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId></LogonTrigger></Triggers>\n" +
            "  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\n" +
            "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled></Settings>\n" +
            "  <Actions Context=\"Author\"><Exec><Command>" + exePath + "</Command><Arguments>--tray</Arguments></Exec></Actions>\n" +
            "</Task>";
        string tmp = Path.Combine(Path.GetTempPath(), "acermode-task.xml");
        File.WriteAllText(tmp, xml, Encoding.Unicode);
        int code = Run("/Create /TN " + TaskName + " /XML \"" + tmp + "\" /F", out var o);
        File.Delete(tmp);
        if (code != 0) throw new InvalidOperationException(o.Trim());
    }

    // O exe novo pode morar em outro caminho: se a tarefa existe, recria apontando para cá.
    public static void Refresh(string exePath) { if (IsOn()) Set(true, exePath); }
}
