using System.Collections.Generic;
using System.Text.Json;

namespace Ampm2.Pm2;

/// <summary>Snapshot of one pm2-managed process, extracted from pm2's formatted process JSON.</summary>
public sealed class Pm2Process
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Namespace { get; init; } = "";
    public int Pid { get; init; }
    public string Status { get; init; } = "";
    public long UptimeSince { get; init; }      // pm_uptime, unix ms
    public long CreatedAt { get; init; }
    public int Restarts { get; init; }
    public int UnstableRestarts { get; init; }
    public string ExecMode { get; init; } = "";
    public string Script { get; init; } = "";
    public string Cwd { get; init; } = "";
    public string Interpreter { get; init; } = "";
    public string Args { get; init; } = "";
    public string NodeArgs { get; init; } = "";
    public string OutLog { get; init; } = "";
    public string ErrLog { get; init; } = "";
    public string Version { get; init; } = "";
    public string NodeVersion { get; init; } = "";
    public string User { get; init; } = "";
    public bool Watch { get; init; }
    public bool AutoRestart { get; init; } = true;
    public string MaxMemoryRestart { get; init; } = "";
    public int Instances { get; init; } = 1;
    /// <summary>Ecosystem-style definition of this app, for ampm2's Saved list.</summary>
    public System.Text.Json.Nodes.JsonObject? Definition { get; init; }
    public double PmCpu { get; init; }
    public long PmMemory { get; init; }

    public static List<Pm2Process> ParseList(JsonElement arr)
    {
        var list = new List<Pm2Process>();
        if (arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var p in arr.EnumerateArray())
        {
            try { list.Add(Parse(p)); } catch { /* skip malformed entries */ }
        }
        list.Sort((a, b) => a.Id.CompareTo(b.Id));
        return list;
    }

    public static Pm2Process Parse(JsonElement p)
    {
        var env = p.TryGetProperty("pm2_env", out var e) ? e : p;
        JsonElement monit = p.TryGetProperty("monit", out var m) ? m : default;
        return new Pm2Process
        {
            Id = Int(p, "pm_id") ?? Int(env, "pm_id") ?? -1,
            Name = Str(p, "name") ?? Str(env, "name") ?? "",
            Namespace = Str(env, "namespace") ?? "",
            Pid = Int(p, "pid") ?? Int(env, "pid") ?? 0,
            Status = Str(env, "status") ?? "unknown",
            UptimeSince = Long(env, "pm_uptime") ?? 0,
            CreatedAt = Long(env, "created_at") ?? 0,
            Restarts = Int(env, "restart_time") ?? 0,
            UnstableRestarts = Int(env, "unstable_restarts") ?? 0,
            ExecMode = (Str(env, "exec_mode") ?? "").Replace("_mode", ""),
            Script = Str(env, "pm_exec_path") ?? "",
            Cwd = Str(env, "pm_cwd") ?? "",
            Interpreter = Str(env, "exec_interpreter") ?? "",
            Args = Joined(env, "args"),
            NodeArgs = Joined(env, "node_args"),
            OutLog = Str(env, "pm_out_log_path") ?? "",
            ErrLog = Str(env, "pm_err_log_path") ?? "",
            Version = Str(env, "version") ?? "",
            NodeVersion = Str(env, "node_version") ?? "",
            User = Str(env, "username") ?? "",
            Watch = Bool(env, "watch"),
            AutoRestart = !env.TryGetProperty("autorestart", out var ar) || ar.ValueKind != JsonValueKind.False,
            MaxMemoryRestart = Raw(env, "max_memory_restart"),
            Instances = Int(env, "instances") ?? 1,
            Definition = SafeDefinition(env),
            PmCpu = monit.ValueKind == JsonValueKind.Object && monit.TryGetProperty("cpu", out var c) && c.TryGetDouble(out var cd) ? cd : 0,
            PmMemory = monit.ValueKind == JsonValueKind.Object && monit.TryGetProperty("memory", out var mm) && mm.TryGetDouble(out var md) ? (long)md : 0,
        };
    }

    private static System.Text.Json.Nodes.JsonObject? SafeDefinition(JsonElement env)
    {
        try { return Sys.AppLibrary.FromPm2Env(env); } catch { return null; }
    }

    private static string? Str(JsonElement o, string k) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement o, string k)
    {
        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(k, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return (int)d;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var i)) return i;
        return null;
    }

    private static long? Long(JsonElement o, string k)
    {
        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(k, out var v)) return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? (long)d : null;
    }

    private static bool Bool(JsonElement o, string k) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(k, out var v) &&
        (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0) ||
         (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s && s != "false"));

    private static string Raw(JsonElement o, string k) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(k, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? v.ToString() : "";

    private static string Joined(JsonElement o, string k)
    {
        if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty(k, out var v)) return "";
        if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
        if (v.ValueKind != JsonValueKind.Array) return "";
        var parts = new List<string>();
        foreach (var x in v.EnumerateArray()) parts.Add(x.ToString());
        return string.Join(" ", parts);
    }
}
