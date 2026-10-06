using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ampm2.Sys;

/// <summary>
/// When each app (by pm2 name) was last seen running, so the Saved list can show "last run …" and sort
/// the most recently used apps first: after a restart, the apps at the top are the ones that ran last.
/// Stored in run-history.json next to the Saved list (not inside it, so exports stay plain ecosystem files).
/// Only what ampm2 sees is recorded: an app that ran while ampm2 was closed is not.
/// </summary>
public sealed class RunHistory
{
    public static string FilePath => Path.Combine(DataPaths.Dir, "run-history.json");

    private readonly Dictionary<string, DateTime> _lastRun = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _runningAtLastTouch = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _savedAt = DateTime.MinValue;
    private bool _dirty;

    /// <summary>How often a still-running app's time is written to disk (it is always current in memory).</summary>
    public static TimeSpan SaveEvery { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Last time the app was seen running (local time), or null if never.</summary>
    public DateTime? LastRun(string name) => _lastRun.TryGetValue(name, out var t) ? t : null;

    public static RunHistory Load()
    {
        var h = new RunHistory();
        try
        {
            if (!File.Exists(FilePath)) return h;
            if (JsonNode.Parse(File.ReadAllText(FilePath))?["lastRun"] is JsonObject o)
                foreach (var (name, v) in o)
                    if (v?.GetValue<string>() is { } s && DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d))
                        h._lastRun[name] = d.ToLocalTime();
        }
        catch { }   // a damaged history only loses the ordering
        h._savedAt = DateTime.Now;
        return h;
    }

    /// <summary>
    /// Records the apps running right now. Saves when one starts running that was not running at the previous
    /// call, and otherwise at most every <see cref="SaveEvery"/>. Returns true when any time changed.
    /// </summary>
    public bool Touch(IEnumerable<string> runningNames, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        var running = new HashSet<string>(runningNames.Where(n => !string.IsNullOrEmpty(n)), StringComparer.OrdinalIgnoreCase);
        bool started = running.Any(n => !_runningAtLastTouch.Contains(n));
        foreach (var n in running) _lastRun[n] = t;
        _runningAtLastTouch = running;
        if (running.Count > 0) _dirty = true;
        if (_dirty && (started || t - _savedAt >= SaveEvery)) Save(t);
        return running.Count > 0;
    }

    /// <summary>Writes pending changes (call on exit so the last minutes are not lost).</summary>
    public void Flush() { if (_dirty) Save(DateTime.Now); }

    private void Save(DateTime now)
    {
        try
        {
            Directory.CreateDirectory(DataPaths.Dir);
            var o = new JsonObject();
            foreach (var (n, d) in _lastRun.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)) o[n] = d.ToUniversalTime().ToString("o");
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, new JsonObject { ["lastRun"] = o }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, true);
            _dirty = false;
            _savedAt = now;
        }
        catch { }
    }

    /// <summary>"running now", "last run today 14:02", "last run yesterday 09:10", "last run Oct 3, 14:02" or "not run yet".</summary>
    public static string Describe(DateTime? lastRun, bool runningNow, DateTime? now = null)
    {
        if (runningNow) return "running now";
        if (lastRun is not { } d) return "not run yet";
        var today = (now ?? DateTime.Now).Date;
        if (d.Date == today) return $"last run today {d:HH:mm}";
        if (d.Date == today.AddDays(-1)) return $"last run yesterday {d:HH:mm}";
        return "last run " + d.ToString(d.Year == today.Year ? "MMM d, HH:mm" : "yyyy-MM-dd");
    }
}
