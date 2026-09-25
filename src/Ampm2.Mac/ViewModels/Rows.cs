using System;
using System.Text.Json.Nodes;
using Ampm2.Pm2;
using Ampm2.Sys;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Media;

namespace Ampm2.Mac.ViewModels;

/// <summary>Status colours; the soft variants are translucent so they read on dark and light.</summary>
public static class StatusBrushes
{
    public static readonly IBrush Online = new SolidColorBrush(Color.Parse("#22B573"));
    public static readonly IBrush Stopped = new SolidColorBrush(Color.Parse("#8B93A5"));
    public static readonly IBrush Errored = new SolidColorBrush(Color.Parse("#EF5350"));
    public static readonly IBrush Launching = new SolidColorBrush(Color.Parse("#E0A526"));
    public static readonly IBrush OnlineSoft = new SolidColorBrush(Color.Parse("#2E22B573"));
    public static readonly IBrush StoppedSoft = new SolidColorBrush(Color.Parse("#268B93A5"));
    public static readonly IBrush ErroredSoft = new SolidColorBrush(Color.Parse("#2EEF5350"));
    public static readonly IBrush LaunchingSoft = new SolidColorBrush(Color.Parse("#2EE0A526"));

    public static IBrush For(string key) => key switch { "Online" => Online, "Errored" => Errored, "Stopped" => Stopped, _ => Launching };
    public static IBrush SoftFor(string key) => key switch { "Online" => OnlineSoft, "Errored" => ErroredSoft, "Stopped" => StoppedSoft, _ => LaunchingSoft };
}

/// <summary>One pm2 process row, updated in place.</summary>
public sealed class ProcessRow : ObservableObject
{
    private const int HistoryLen = 60;
    private readonly double[] _cpuHist = new double[HistoryLen];
    private readonly double[] _memHist = new double[HistoryLen];
    private int _histCount;

    public ProcessRow(Pm2Process p) => _p = p;
    private Pm2Process _p;
    public Pm2Process P => _p;
    public int Id => _p.Id;
    public string IdText => _p.Id.ToString();
    public string Name => _p.Name;
    public bool IsOnline => _p.Status == "online";
    public bool IsErrored => _p.Status == "errored";
    public bool CanStart => !IsOnline && _p.Status != "launching";
    public bool CanStop => IsOnline || _p.Status is "launching" or "waiting restart";
    public bool IsCluster => _p.ExecMode == "cluster";
    public string Mode => IsCluster ? $"cluster ×{Math.Max(1, _p.Instances)}" : "fork";
    public string Namespace => _p.Namespace is "" or "default" ? "" : _p.Namespace;
    public bool HasNamespace => Namespace.Length > 0;
    public string Script => _p.Script;
    public int Restarts => _p.Restarts;
    public bool HasRestarts => _p.Restarts > 0;
    public string PidText => _p.Pid > 0 && IsOnline ? _p.Pid.ToString() : "—";
    public string StatusKey => _p.Status switch
    {
        "online" => "Online",
        "stopped" or "stopping" => "Stopped",
        "errored" or "one-launch-status" => "Errored",
        _ => "Launching",
    };
    public string StatusText => _p.Status == "waiting restart" ? "waiting" : _p.Status;
    public IBrush StatusBrush => StatusBrushes.For(StatusKey);
    public IBrush StatusSoftBrush => StatusBrushes.SoftFor(StatusKey);
    public IBrush RestartsBrush => HasRestarts ? StatusBrushes.Launching : StatusBrushes.Stopped;

    private double _cpu;
    public double Cpu { get => _cpu; private set { if (Set(ref _cpu, value)) { Raise(nameof(CpuText)); Raise(nameof(CpuBarWidth)); } } }
    public string CpuText => IsOnline ? $"{_cpu:0.#}%" : "—";
    public double CpuBarWidth => Math.Min(1, _cpu / 100.0) * 56;
    private long _mem;
    public long Memory { get => _mem; private set { if (Set(ref _mem, value)) Raise(nameof(MemText)); } }
    public string MemText => IsOnline ? Fmt.Bytes(_mem) : "—";
    private int _tree;
    public string TreeText => _tree > 1 ? $"{_tree} processes in tree" : "1 process";
    public string UptimeText => IsOnline && _p.UptimeSince > 0
        ? Fmt.Duration(DateTime.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(_p.UptimeSince).UtcDateTime) : "—";
    public string StartedText => IsOnline ? Fmt.Unix(_p.UptimeSince) : "—";
    public string CreatedText => Fmt.Unix(_p.CreatedAt);
    public string RestartsText => $"{_p.Restarts} total · {_p.UnstableRestarts} unstable";
    public string VersionsText => $"app {(_p.Version.Length > 0 ? _p.Version : "—")} · node {(_p.NodeVersion.Length > 0 ? _p.NodeVersion : "—")}";
    public string AutoRestartText => _p.AutoRestart ? "yes" : "no";
    public string WatchText => _p.Watch ? "yes" : "no";

    private Points _cpuLine = new(), _memLine = new();
    public Points CpuLine { get => _cpuLine; private set => Set(ref _cpuLine, value); }
    public Points MemLine { get => _memLine; private set => Set(ref _memLine, value); }

    private bool _busy;
    public bool Busy { get => _busy; set => Set(ref _busy, value); }

    public void Update(Pm2Process p)
    {
        var old = _p;
        _p = p;
        if (old.Status != p.Status || old.Pid != p.Pid) { Cpu = 0; Memory = 0; }
        Raise(string.Empty);
    }

    public void ApplyMetrics(double cpu, long mem, int count)
    {
        Cpu = cpu; Memory = mem;
        if (_tree != count) { _tree = count; Raise(nameof(TreeText)); }
        Array.Copy(_cpuHist, 1, _cpuHist, 0, HistoryLen - 1);
        Array.Copy(_memHist, 1, _memHist, 0, HistoryLen - 1);
        _cpuHist[HistoryLen - 1] = cpu;
        _memHist[HistoryLen - 1] = mem;
        if (_histCount < HistoryLen) _histCount++;
    }

    public void ClearMetrics() { Cpu = 0; Memory = 0; }
    public void Tick() { if (IsOnline) Raise(nameof(UptimeText)); }

    public void BuildSparklines(double w, double h)
    {
        CpuLine = Line(_cpuHist, w, h, Math.Max(100, Max(_cpuHist)));
        MemLine = Line(_memHist, w, h, Max(_memHist) * 1.3);
    }

    private static double Max(double[] a) { double m = 0; foreach (var v in a) if (v > m) m = v; return m; }

    private Points Line(double[] data, double w, double h, double max)
    {
        var pts = new Points();
        if (max <= 0) max = 1;
        int n = Math.Max(_histCount, 1), start = HistoryLen - n;
        double Y(double v) => h - Math.Min(1, v / max) * (h - 2) - 1;
        pts.Add(new Point(0, h));
        if (n == 1) { pts.Add(new Point(0, Y(data[HistoryLen - 1]))); pts.Add(new Point(w, Y(data[HistoryLen - 1]))); }
        else
        {
            double step = w / (n - 1);
            for (int k = 0; k < n; k++) pts.Add(new Point(k * step, Y(data[start + k])));
        }
        pts.Add(new Point(w, h));
        return pts;
    }
}

/// <summary>One Saved-list row.</summary>
public sealed class SavedRow : ObservableObject
{
    public SavedRow(string name, JsonObject def, DateTime updated) { Name = name; SetDef(def, updated); }
    public string Name { get; }
    public JsonObject Def { get; private set; } = new();
    public string Script { get; private set; } = "";
    public string Cwd { get; private set; } = "";
    public string Json { get; private set; } = "";
    public string Summary { get; private set; } = "";
    public string UpdatedText { get; private set; } = "";

    public void SetDef(JsonObject def, DateTime updated)
    {
        Def = def;
        Script = def["script"]?.ToString() ?? "";
        Cwd = def["cwd"]?.ToString() ?? "";
        Json = def.ToJsonString(AppLibrary.Indented);
        UpdatedText = updated == DateTime.MinValue ? "" : "saved " + updated.ToString(updated.Year == DateTime.Now.Year ? "MMM d, HH:mm" : "yyyy-MM-dd");
        var mode = def["exec_mode"]?.ToString() == "cluster" ? $"cluster ×{def["instances"]?.ToString() ?? "?"}" : "fork";
        int env = def["env"] is JsonObject e ? e.Count : 0;
        Summary = mode + (env > 0 ? $" · {env} env var{(env == 1 ? "" : "s")}" : "");
        Raise(string.Empty);
    }

    private string _state = "unknown";   // online | stopped | errored | missing | unknown
    public string State
    {
        get => _state;
        set { if (Set(ref _state, value)) { Raise(nameof(StateText)); Raise(nameof(StateBrush)); Raise(nameof(StateSoftBrush)); Raise(nameof(IsMissing)); } }
    }
    public bool IsMissing => _state == "missing";
    public string StateText => _state switch { "missing" => "not in pm2", "unknown" => "pm2 not connected", var s => "in pm2 · " + s };
    private string Key => _state switch { "online" => "Online", "errored" => "Errored", "missing" => "Launching", _ => "Stopped" };
    public IBrush StateBrush => StatusBrushes.For(Key);
    public IBrush StateSoftBrush => StatusBrushes.SoftFor(Key);
}
