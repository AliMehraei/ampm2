using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ampm2.Pm2;

public readonly record struct CliResult(int ExitCode, string Output)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// Runs the pm2 / npm / winget command lines for the rare operations the RPC cannot do cheaply
/// (start a new app, save, resurrect, flush, install). Everything frequent goes over the pipe/socket.
/// </summary>
public static class Pm2Cli
{
    private static readonly bool Win = OperatingSystem.IsWindows();
    private static readonly char Sep = Path.PathSeparator;

    public static string? FindOnPath(params string[] names)
    {
        var dirs = new List<string>(MergedPath().Split(Sep, StringSplitOptions.RemoveEmptyEntries));
        if (Win) dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"));
        foreach (var d in dirs)
            foreach (var n in names)
            {
                try
                {
                    var f = Path.Combine(d.Trim('"'), n);
                    if (File.Exists(f)) return f;
                }
                catch { }
            }
        return null;
    }

    /// <summary>pm2 command line. AMPM2_PM2 overrides it (the test harness uses tools\test-pm2.cmd).</summary>
    public static string? Pm2Path =>
        Environment.GetEnvironmentVariable("AMPM2_PM2") is { Length: > 0 } o && File.Exists(o) ? o
        : Win ? FindOnPath("pm2.cmd", "pm2.exe") : FindOnPath("pm2");
    public static string? NodePath => Win ? FindOnPath("node.exe") : FindOnPath("node");
    public static string? NpmPath => Win ? FindOnPath("npm.cmd") : FindOnPath("npm");
    public static string? WingetPath => !Win ? null : FindOnPath("winget.exe") ??
        (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe"))
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe") : null);
    public static string? BrewPath => Win ? null : new[] { "/opt/homebrew/bin/brew", "/usr/local/bin/brew" }.FirstOrDefault(File.Exists);

    /// <summary>Quotes one argument for cmd.exe + the CRT parser (Windows only).</summary>
    public static string Quote(string a)
    {
        if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"', '&', '|', '<', '>', '^', '(', ')' }) < 0) return a;
        var sb = new StringBuilder("\"");
        int bs = 0;
        foreach (var ch in a)
        {
            if (ch == '\\') { bs++; continue; }
            if (ch == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); bs = 0; continue; }
            sb.Append('\\', bs); bs = 0; sb.Append(ch);
        }
        sb.Append('\\', bs * 2).Append('"');
        return sb.ToString();
    }

    public static Task<CliResult> Pm2Async(IEnumerable<string> args, string? cwd = null)
    {
        var pm2 = Pm2Path ?? throw new InvalidOperationException("pm2 is not installed.");
        return RunAsync(pm2, args, cwd);
    }

    /// <summary>Runs a program hidden, capturing stdout+stderr. On Windows a .cmd goes through cmd.exe.</summary>
    public static async Task<CliResult> RunAsync(string file, IEnumerable<string> args, string? cwd = null, int timeoutMs = 10 * 60 * 1000)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = cwd != null && Directory.Exists(cwd) ? cwd : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (Win)
        {
            var argLine = string.Join(" ", args.Select(Quote));
            if (file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                psi.Arguments = "/d /s /c \"" + Quote(file) + " " + argLine + "\"";
            }
            else
            {
                psi.FileName = file;
                psi.Arguments = argLine;
            }
        }
        else
        {
            psi.FileName = file;
            foreach (var a in args) psi.ArgumentList.Add(a);   // no shell, no quoting
        }
        psi.Environment["PATH"] = MergedPath();
        // No extra variables here: pm2 copies the CLI's environment into apps it starts (colors are stripped from output instead).

        using var p = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        var exited = await Task.Run(() => p.WaitForExit(timeoutMs)).ConfigureAwait(false);
        if (!exited) { try { p.Kill(true); } catch { } return new CliResult(-1, sb + "\n(timed out)"); }
        p.WaitForExit();
        return new CliResult(p.ExitCode, StripAnsi(sb.ToString()).Trim());
    }

    /// <summary>
    /// The PATH child processes get. Windows: machine + user PATH read fresh from the registry (so a just-installed
    /// Node.js is found), then this process's. macOS/Linux: the login shell's PATH (a Finder-launched app only gets
    /// /usr/bin:/bin:…, which hides Homebrew / nvm), the usual Node locations, then this process's.
    /// </summary>
    public static string MergedPath()
    {
        var seen = new HashSet<string>(Win ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var parts = new List<string>();
        void Add(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            foreach (var d in path.Split(Sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var x = Environment.ExpandEnvironmentVariables(d);
                if (seen.Add(x)) parts.Add(x);
            }
        }
        if (Win)
        {
            Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
            Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));
            Add(Environment.GetEnvironmentVariable("PATH"));
            Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
        }
        else
        {
            Add(LoginShellVariable("PATH"));
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Add(string.Join(Sep, new[] { "/opt/homebrew/bin", "/usr/local/bin", Path.Combine(home, ".volta", "bin"), Path.Combine(home, ".bun", "bin"), Path.Combine(home, "n", "bin") }));
            // nvm: newest installed version
            try
            {
                var nvm = Path.Combine(home, ".nvm", "versions", "node");
                if (Directory.Exists(nvm))
                    Add(Directory.GetDirectories(nvm).OrderByDescending(v => v, StringComparer.Ordinal).Select(v => Path.Combine(v, "bin")).FirstOrDefault());
            }
            catch { }
            Add(Environment.GetEnvironmentVariable("PATH"));
            Add("/usr/bin:/bin:/usr/sbin:/sbin");
        }
        return string.Join(Sep, parts);
    }

    // ---------------- login shell environment (macOS/Linux) ----------------

    private static Dictionary<string, string>? _shellEnv;
    private static readonly object ShellLock = new();

    /// <summary>A variable as the user's interactive login shell sees it (cached; empty on Windows).</summary>
    public static string? LoginShellVariable(string name) => LoginShellEnvironment().TryGetValue(name, out var v) ? v : null;

    /// <summary>
    /// The environment of `$SHELL -ilc env`, read once (~100 ms). Used for PATH, PM2_HOME, and to tell an app's own
    /// variables from inherited ones when saving definitions.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LoginShellEnvironment()
    {
        if (Win) return new Dictionary<string, string>();
        lock (ShellLock)
        {
            if (_shellEnv != null) return _shellEnv;
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var shell = Environment.GetEnvironmentVariable("SHELL");
                if (string.IsNullOrEmpty(shell) || !File.Exists(shell)) shell = File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/sh";
                var psi = new ProcessStartInfo(shell)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    RedirectStandardInput = true, StandardOutputEncoding = Encoding.UTF8,
                };
                // markers keep rc-file chatter (motd, prompts) out of the result; env -0 survives newlines in values
                psi.ArgumentList.Add("-ilc");
                psi.ArgumentList.Add("printf '__AMPM2_BEGIN__'; env -0; printf '__AMPM2_END__'");
                using var p = Process.Start(psi)!;
                p.StandardInput.Close();
                var outTask = p.StandardOutput.ReadToEndAsync();
                _ = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(5000)) { try { p.Kill(true); } catch { } }
                var text = outTask.Wait(1000) ? outTask.Result : "";
                int a = text.IndexOf("__AMPM2_BEGIN__", StringComparison.Ordinal), b = text.LastIndexOf("__AMPM2_END__", StringComparison.Ordinal);
                if (a >= 0 && b > a)
                    foreach (var entry in text[(a + 15)..b].Split('\0', StringSplitOptions.RemoveEmptyEntries))
                    {
                        int eq = entry.IndexOf('=');
                        if (eq > 0) d[entry[..eq].Trim('\n', '\r')] = entry[(eq + 1)..];
                    }
            }
            catch { }
            return _shellEnv = d;
        }
    }

    public static string StripAnsi(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\u001b' && i + 1 < s.Length && s[i + 1] == '[')
            {
                i += 2;
                while (i < s.Length && !(s[i] >= '@' && s[i] <= '~')) i++;
                continue;
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
