using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading.Tasks;
using Ampm2.Pm2;

namespace Ampm2.Sys;

public static class Elevation
{
    public const string TaskName = "ampm2 (elevated)";

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    public static string ExePath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;

    /// <summary>Starts a new elevated copy (UAC prompt). Returns false if the user declined.</summary>
    public static bool RelaunchElevated(string args = "")
    {
        try
        {
            Process.Start(new ProcessStartInfo(ExePath, args + " --relaunched") { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch { return false; }
    }

    /// <summary>A scheduled task that runs ampm2 with highest privileges lets shortcuts start it elevated without a UAC prompt.</summary>
    public static async Task<bool> ElevatedTaskExistsAsync()
    {
        var r = await Pm2Cli.RunAsync("schtasks.exe", new[] { "/Query", "/TN", TaskName }, timeoutMs: 15000).ConfigureAwait(false);
        return r.Ok;
    }

    /// <summary>True only if the task exists AND launches this very exe (a task left by another copy, e.g. the portable build, is ignored).</summary>
    public static async Task<bool> ElevatedTaskIsMineAsync()
    {
        var r = await Pm2Cli.RunAsync("schtasks.exe", new[] { "/Query", "/TN", TaskName, "/XML" }, timeoutMs: 15000).ConfigureAwait(false);
        if (!r.Ok) return false;
        int a = r.Output.IndexOf("<Command>", StringComparison.OrdinalIgnoreCase), b = r.Output.IndexOf("</Command>", StringComparison.OrdinalIgnoreCase);
        if (a < 0 || b < a) return false;
        var cmd = System.Net.WebUtility.HtmlDecode(r.Output[(a + 9)..b]).Trim().Trim('"');
        return string.Equals(System.IO.Path.GetFullPath(cmd), System.IO.Path.GetFullPath(ExePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Registers an on-demand task (no trigger) that runs ampm2 elevated in the user's session.
    /// Uses task XML: locale-independent, no quoting pitfalls, never prompts for a password.
    /// </summary>
    public static async Task<CliResult> RegisterElevatedTaskAsync()
    {
        var user = System.Security.SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var exe = System.Security.SecurityElement.Escape(ExePath);
        var dir = System.Security.SecurityElement.Escape(System.IO.Path.GetDirectoryName(ExePath) ?? "");
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts ampm2 as administrator without a UAC prompt (created by ampm2).</Description></RegistrationInfo>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
                <AllowStartOnDemand>true</AllowStartOnDemand>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{exe}</Command><Arguments>--from-task</Arguments><WorkingDirectory>{dir}</WorkingDirectory></Exec>
              </Actions>
            </Task>
            """;
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ampm2-task.xml");
        await System.IO.File.WriteAllTextAsync(file, xml, System.Text.Encoding.Unicode).ConfigureAwait(false);
        try
        {
            return await Pm2Cli.RunAsync("schtasks.exe", new[] { "/Create", "/F", "/TN", TaskName, "/XML", file }, timeoutMs: 30000).ConfigureAwait(false);
        }
        finally { try { System.IO.File.Delete(file); } catch { } }
    }

    public static Task<CliResult> UnregisterElevatedTaskAsync() =>
        Pm2Cli.RunAsync("schtasks.exe", new[] { "/Delete", "/F", "/TN", TaskName }, timeoutMs: 15000);

    public static Task<CliResult> RunElevatedTaskAsync() =>
        Pm2Cli.RunAsync("schtasks.exe", new[] { "/Run", "/TN", TaskName }, timeoutMs: 15000);
}
