using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ampm2.Pm2;

namespace Ampm2.Sys;

/// <summary>
/// ampm2's own copy of the process list ("Saved list"), independent of pm2's dump.pm2.
/// Stored as a pm2 ecosystem file (saved-list.json in DataPaths.Dir), so an export is directly usable
/// with `pm2 start file.json`, and the list survives pm2 losing its dump.
/// </summary>
public sealed class AppLibrary
{
    public static string FilePath => Path.Combine(DataPaths.Dir, "saved-list.json");
    /// <summary>Raised when the saved list cannot be read (title, message); the host shows it.</summary>
    public static event Action<string, string>? Warning;
    private readonly Dictionary<string, (JsonObject Def, DateTime Updated)> _apps = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<(string Name, JsonObject Def, DateTime Updated)> Apps =>
        _apps.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).Select(k => (k.Key, k.Value.Def, k.Value.Updated));
    public int Count => _apps.Count;
    public bool Contains(string name) => _apps.ContainsKey(name);
    public JsonObject? Get(string name) => _apps.TryGetValue(name, out var v) ? v.Def : null;

    public static AppLibrary Load()
    {
        var lib = new AppLibrary();
        try
        {
            if (!File.Exists(FilePath)) return lib;
            var root = JsonNode.Parse(File.ReadAllText(FilePath), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var updated = root?["ampm2"]?["updated"] as JsonObject;
            if (root?["apps"] is JsonArray arr)
                foreach (var a in arr)
                    if (a is JsonObject o && Name(o) is { } n)
                    {
                        var when = updated?[n]?.GetValue<string>() is { } s && DateTime.TryParse(s, out var d) ? d : DateTime.MinValue;
                        lib._apps[n] = ((JsonObject)o.DeepClone(), when);
                    }
        }
        catch (Exception ex)
        {
            // keep the broken file for the user instead of overwriting it on the next save
            try { File.Copy(FilePath, FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); } catch { }
            Warning?.Invoke("Saved list could not be read", ex.Message);
        }
        return lib;
    }

    public void Save()
    {
        Directory.CreateDirectory(DataPaths.Dir);
        var updated = new JsonObject();
        foreach (var (n, _, u) in Apps) updated[n] = u.ToString("o");
        var root = new JsonObject
        {
            ["apps"] = new JsonArray(Apps.Select(a => (JsonNode)a.Def.DeepClone()).ToArray()),
            ["ampm2"] = new JsonObject { ["version"] = 1, ["updated"] = updated },
        };
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(Indented), new UTF8Encoding(false));
        File.Move(tmp, FilePath, true);   // atomic replace: a crash never leaves a half-written list
    }

    /// <summary>Adds or replaces an app. Returns true when something changed.</summary>
    public bool Upsert(JsonObject def)
    {
        var n = Name(def);
        if (n == null) return false;
        if (_apps.TryGetValue(n, out var cur) && JsonNode.DeepEquals(cur.Def, def)) return false;
        _apps[n] = ((JsonObject)def.DeepClone(), DateTime.Now);
        return true;
    }

    public bool Remove(string name) => _apps.Remove(name);

    public static string? Name(JsonObject o) => o["name"] is JsonValue v && v.TryGetValue<string>(out var s) && s.Trim().Length > 0 ? s.Trim() : null;

    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>An ecosystem document for the given apps: {"apps":[...]}.</summary>
    public static string Ecosystem(IEnumerable<JsonObject> defs) =>
        new JsonObject { ["apps"] = new JsonArray(defs.Select(d => (JsonNode)d.DeepClone()).ToArray()) }.ToJsonString(Indented);

    // ---------------- capture from pm2 ----------------

    /// <summary>
    /// Builds an ecosystem app definition from a pm2_env object (live process or a dump.pm2 entry).
    /// Only keeps settings that differ from pm2's defaults, and only environment variables that belong to the app.
    /// </summary>
    public static JsonObject? FromPm2Env(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        var name = S(e, "name");
        var script = S(e, "pm_exec_path") ?? S(e, "script");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(script)) return null;

        // Apps created by ampm2 keep their exact original config (including env) in apps\<name>.json.
        if (OwnConfig(name) is { } own) return own;

        var o = new JsonObject { ["name"] = name, ["script"] = script };
        if ((S(e, "pm_cwd") ?? S(e, "cwd")) is { } cwd) o["cwd"] = cwd;
        if (e.TryGetProperty("args", out var args))
        {
            if (args.ValueKind == JsonValueKind.Array && args.GetArrayLength() > 0) o["args"] = JsonNode.Parse(args.GetRawText());
            else if (args.ValueKind == JsonValueKind.String && args.GetString()!.Length > 0) o["args"] = args.GetString();
        }
        if ((S(e, "exec_interpreter") ?? S(e, "interpreter")) is { } interp) o["interpreter"] = interp;
        if (e.TryGetProperty("node_args", out var na) && na.ValueKind == JsonValueKind.Array && na.GetArrayLength() > 0) o["node_args"] = JsonNode.Parse(na.GetRawText());
        var mode = (S(e, "exec_mode") ?? "fork").Replace("_mode", "");
        o["exec_mode"] = mode;
        if (mode == "cluster" && e.TryGetProperty("instances", out var inst) && inst.ValueKind == JsonValueKind.Number)
            o["instances"] = inst.GetInt32() <= 0 ? "max" : inst.GetInt32();
        if (S(e, "namespace") is { } ns && ns != "default") o["namespace"] = ns;
        if (e.TryGetProperty("watch", out var w) && (w.ValueKind == JsonValueKind.True || w.ValueKind == JsonValueKind.Array)) o["watch"] = JsonNode.Parse(w.GetRawText());
        if (e.TryGetProperty("ignore_watch", out var iw) && iw.ValueKind == JsonValueKind.Array) o["ignore_watch"] = JsonNode.Parse(iw.GetRawText());
        if (e.TryGetProperty("autorestart", out var ar) && ar.ValueKind == JsonValueKind.False) o["autorestart"] = false;
        foreach (var k in new[] { "max_memory_restart", "restart_delay", "min_uptime", "max_restarts", "kill_timeout", "listen_timeout",
                                  "exp_backoff_restart_delay", "cron_restart", "log_date_format", "time", "wait_ready", "windowsHide" })
            if (e.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.Number or JsonValueKind.String or JsonValueKind.True or JsonValueKind.False)
                o[k] = JsonNode.Parse(v.GetRawText());
        foreach (var (src, dst) in new[] { ("pm_out_log_path", "out_file"), ("pm_err_log_path", "error_file") })
            if (S(e, src) is { } p && !IsDefaultLogPath(p)) o[dst] = p;

        if (e.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
        {
            var appEnv = new JsonObject();
            var system = SystemEnvironment();
            foreach (var p in env.EnumerateObject())
            {
                if (p.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)) continue;
                var val = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText();
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || IsShellOrPm2Variable(p.Name)) continue;
                if (system.TryGetValue(p.Name, out var sys) && sys == val) continue;   // inherited, not the app's own
                appEnv[p.Name] = val;
            }
            if (appEnv.Count > 0) o["env"] = appEnv;
        }
        return o;
    }

    private static JsonObject? OwnConfig(string name)
    {
        try
        {
            var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
            var f = Path.Combine(DataPaths.Dir, "apps", safe + ".json");
            if (!File.Exists(f)) return null;
            if (JsonNode.Parse(File.ReadAllText(f))?["apps"] is JsonArray a && a.Count > 0 && a[0] is JsonObject o && Name(o) == name)
                return (JsonObject)o.DeepClone();
        }
        catch { }
        return null;
    }

    private static bool IsDefaultLogPath(string p)
    {
        // pm2's default: <PM2_HOME>/logs/<name>-out.log
        var dir = Path.GetDirectoryName(p) ?? "";
        if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(Path.Combine(Pm2Endpoints.Home, "logs")), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return true;
        var parent = Path.GetFileName(Path.GetDirectoryName(dir) ?? "");
        return Path.GetFileName(dir).Equals("logs", StringComparison.OrdinalIgnoreCase) && parent.Contains("pm2", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly HashSet<string> DenyExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "OLDPWD", "PWD", "SHLVL", "_", "SHELL", "PS1", "PROMPT", "SESSIONNAME", "HOSTNAME", "DISPLAY", "EXEPATH",
        "PLINK_PROTOCOL", "SSH_ASKPASS", "CONFIG_SITE", "MANPATH", "INFOPATH", "ACLOCAL_PATH", "TMPDIR", "LANG", "HOME",
        "AI_AGENT", "POWERSHELL_DISTRIBUTION_CHANNEL", "NODE_APP_INSTANCE", "unique_id", "pm_id", "name", "namespace",
        "status", "PATH", "PATHEXT", "GIT_EDITOR", "LOGONSERVER", "wkPath", "TERMINAL_EMULATOR", "COLORTERM", "LC_ALL",
        "ORIGINAL_PATH", "ORIGINAL_TEMP", "ORIGINAL_TMP", "PSModulePath", "COREPACK_ENABLE_AUTO_PIN",
        "FORCE_COLOR", "NO_COLOR",   // injected by early ampm2 builds into apps they started
        "USER", "LOGNAME", "SSH_AUTH_SOCK", "COMMAND_MODE", "SECURITYSESSIONID", "MallocNanoZone", "OSLogRateLimit",
        "LESS", "PAGER", "LSCOLORS", "LS_COLORS", "ZSH", "EDITOR", "VISUAL", "MAIL", "XDG_SESSION_ID", "XDG_RUNTIME_DIR",
        "DBUS_SESSION_BUS_ADDRESS", "WSLENV", "WSL_DISTRO_NAME", "WSL_INTEROP", "NAME", "HOSTTYPE", "MOTD_SHOWN",
    };
    private static readonly string[] DenyPrefix = { "PM2_", "AMPM2_", "XPC_", "__CF", "HOMEBREW_", "ITERM", "TERM_PROGRAM", "P9K", "LC_", "SSH_", "Apple_", "XDG_", "npm_", "NVM_", "VSCODE_", "WT_", "MSYS", "MINGW", "TERM_", "COPILOT_", "EFC_", "PKG_CONFIG", "__", "=" };
    private static readonly string[] DenySuffix = { "_VM_OPTIONS" };

    private static bool IsShellOrPm2Variable(string k) =>
        DenyExact.Contains(k) || DenyPrefix.Any(p => k.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
        DenySuffix.Any(s => k.EndsWith(s, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string>? _system;
    private static DateTime _systemAt;
    private static Dictionary<string, string> SystemEnvironment()
    {
        if (_system != null && DateTime.UtcNow - _systemAt < TimeSpan.FromMinutes(2)) return _system;
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scopes = OperatingSystem.IsWindows()
            ? new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Process }
            : new[] { EnvironmentVariableTarget.Process };
        foreach (var scope in scopes)
        {
            try
            {
                foreach (DictionaryEntry kv in Environment.GetEnvironmentVariables(scope))
                    if (kv.Key is string k && kv.Value is string v) d[k] = Environment.ExpandEnvironmentVariables(v);
            }
            catch { }
        }
        // a Finder-launched Mac app has almost no environment; the login shell has what a terminal-started daemon inherited
        foreach (var kv in Pm2Cli.LoginShellEnvironment()) d.TryAdd(kv.Key, kv.Value);
        _systemAt = DateTime.UtcNow;
        return _system = d;
    }

    private static string? S(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    // ---------------- import ----------------

    /// <summary>
    /// Reads app definitions from an ecosystem .json / .config.js / .cjs, a bare array, or pm2's dump.pm2.
    /// JavaScript configs are evaluated with Node.js (they are the user's own files).
    /// </summary>
    public static List<JsonObject> ReadFile(string path)
    {
        string json;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".js" or ".cjs" or ".mjs")
        {
            var node = Pm2Cli.NodePath ?? throw new InvalidOperationException("Node.js is needed to read a .js ecosystem file.");
            var psi = new System.Diagnostics.ProcessStartInfo(node)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(path)!, StandardOutputEncoding = Encoding.UTF8,
            };
            psi.Environment["PATH"] = Pm2Cli.MergedPath();
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add("const m=require(process.argv[1]);Promise.resolve((m&&m.default)||m).then(c=>process.stdout.write(JSON.stringify(c)))");
            psi.ArgumentList.Add(Path.GetFullPath(path));
            using var p = System.Diagnostics.Process.Start(psi)!;
            json = p.StandardOutput.ReadToEnd();
            var err = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } throw new TimeoutException("Reading the .js file took too long."); }
            if (p.ExitCode != 0) throw new InvalidOperationException(err.Trim().Split('\n').FirstOrDefault() ?? "node failed");
        }
        else json = File.ReadAllText(path);

        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        JsonArray? arr = root as JsonArray ?? root?["apps"] as JsonArray;
        if (arr == null && root is JsonObject single && (single["script"] != null || single["pm_exec_path"] != null)) arr = new JsonArray(single.DeepClone());
        if (arr == null) throw new InvalidOperationException("No apps found. Expected an ecosystem file ({\"apps\":[…]}), an array of apps, or pm2's dump.pm2.");

        var baseDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var list = new List<JsonObject>();
        void Add(JsonObject d) { ResolvePaths(d, baseDir); list.Add(d); }
        foreach (var a in arr)
        {
            if (a is not JsonObject o) continue;
            if (o["pm_exec_path"] != null)   // a dump.pm2 entry: a full pm2_env
            {
                using var doc = JsonDocument.Parse(o.ToJsonString());
                if (FromPm2Env(doc.RootElement) is { } d) list.Add(d);
            }
            else if (Name(o) != null && o["script"] != null) Add((JsonObject)o.DeepClone());
            else if (o["script"] is JsonValue sv && sv.TryGetValue<string>(out var sc))
            {
                var c = (JsonObject)o.DeepClone();
                c["name"] = Path.GetFileNameWithoutExtension(sc);   // pm2 names unnamed apps after the script
                Add(c);
            }
        }
        return list;
    }

    /// <summary>pm2 resolves relative cwd/script against the ecosystem file; make them absolute so the saved copy works from anywhere.</summary>
    private static void ResolvePaths(JsonObject o, string baseDir)
    {
        string cwd = baseDir;
        if (o["cwd"] is JsonValue cv && cv.TryGetValue<string>(out var c) && c.Length > 0)
            cwd = Path.IsPathRooted(c) ? c : Path.GetFullPath(Path.Combine(baseDir, c));
        o["cwd"] = cwd;
        if (o["script"] is JsonValue sv && sv.TryGetValue<string>(out var s) && s.Length > 0 && !Path.IsPathRooted(s) &&
            (s.StartsWith('.') || s.Contains('/') || s.Contains('\\') || File.Exists(Path.Combine(cwd, s))))
            o["script"] = Path.GetFullPath(Path.Combine(cwd, s));   // a bare command like "npm" or "python" stays as is
    }
}
