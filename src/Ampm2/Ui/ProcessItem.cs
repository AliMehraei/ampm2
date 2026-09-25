using System;
using System.Windows;
using System.Windows.Media;
using Ampm2.Pm2;
using Ampm2.Sys;

namespace Ampm2.Ui;

/// <summary>One row in the process list. Updated in place so the UI never rebuilds rows.</summary>
public sealed class ProcessItem : ObservableObject
{
    private const int HistoryLen = 60;
    private readonly double[] _cpuHist = new double[HistoryLen];
    private readonly double[] _memHist = new double[HistoryLen];
    private int _histCount;

    public ProcessItem(Pm2Process p) { _p = p; }

    private Pm2Process _p;
    public Pm2Process P => _p;
    public int Id => _p.Id;
    public string IdText => _p.Id.ToString();
    public string Name => _p.Name;
    public string Status => _p.Status;
    public string Mode => _p.ExecMode == "cluster" ? $"cluster ×{Math.Max(1, _p.Instances)}" : (_p.ExecMode.Length == 0 ? "fork" : _p.ExecMode);
    public bool IsCluster => _p.ExecMode == "cluster";
    public string PidText => _p.Pid > 0 && IsOnline ? _p.Pid.ToString() : "—";
    public int Restarts => _p.Restarts;
    public bool HasRestarts => _p.Restarts > 0;
    public bool IsOnline => _p.Status == "online";
    public bool IsStopped => _p.Status == "stopped";
    public bool IsErrored => _p.Status == "errored";
    public bool CanStart => !IsOnline && _p.Status != "launching";
    public bool CanStop => IsOnline || _p.Status == "launching" || _p.Status == "waiting restart";
    public string Namespace => _p.Namespace is "" or "default" ? "" : _p.Namespace;

    public string StatusKey => _p.Status switch
    {
        "online" => "Online",
        "stopped" or "stopping" => "Stopped",
        "errored" or "one-launch-status" => "Errored",
        _ => "Launching",
    };

    public Brush StatusBrush => (Brush)Application.Current.Resources[StatusKey];
    public Brush StatusSoftBrush => (Brush)Application.Current.Resources[StatusKey + "Soft"];
    public string StatusText => _p.Status == "waiting restart" ? "waiting" : _p.Status;

    private double _cpu;
    public double Cpu { get => _cpu; private set { if (Set(ref _cpu, value)) { Raise(nameof(CpuText)); Raise(nameof(CpuBar)); } } }
    public string CpuText => IsOnline ? $"{_cpu:0.#}%" : "—";
    public double CpuBar => Math.Min(1, _cpu / 100.0);

    private long _mem;
    public long Memory { get => _mem; private set { if (Set(ref _mem, value)) Raise(nameof(MemText)); } }
    public string MemText => IsOnline ? Fmt.Bytes(_mem) : "—";

    private int _treeCount;
    public int TreeCount { get => _treeCount; private set { if (Set(ref _treeCount, value)) Raise(nameof(TreeText)); } }
    public string TreeText => _treeCount > 1 ? $"{_treeCount} processes in tree" : "1 process";

    public string UptimeText => IsOnline && _p.UptimeSince > 0
        ? Fmt.Duration(DateTime.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(_p.UptimeSince).UtcDateTime) : "—";

    public string CreatedText => Fmt.Unix(_p.CreatedAt);
    public string AutoRestartText => _p.AutoRestart ? "yes" : "no";
    public string WatchText => _p.Watch ? "yes" : "no";
    public string MaxMemText => _p.MaxMemoryRestart.Length == 0 ? "—" :
        long.TryParse(_p.MaxMemoryRestart, out var b) ? Fmt.Bytes(b) : _p.MaxMemoryRestart;
    public string StartedText => IsOnline ? Fmt.Unix(_p.UptimeSince) : "—";

    private PointCollection _cpuLine = new(), _memLine = new();
    public PointCollection CpuLine { get => _cpuLine; private set => Set(ref _cpuLine, value); }
    public PointCollection MemLine { get => _memLine; private set => Set(ref _memLine, value); }
    private string _memPeak = "—";
    public string MemPeak { get => _memPeak; private set => Set(ref _memPeak, value); }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    private bool _busy;
    public bool Busy { get => _busy; set => Set(ref _busy, value); }

    public void Update(Pm2Process p)
    {
        var old = _p;
        _p = p;
        if (old.Status != p.Status || old.Pid != p.Pid) { Cpu = 0; Memory = IsOnline ? p.PmMemory : 0; }
        Raise(string.Empty); // refresh all bindings on this row (cheap: few rows, rare)
    }

    public void ApplyMetrics(double cpu, long mem, int count, bool recordHistory)
    {
        Cpu = cpu; Memory = mem; TreeCount = count;
        if (!recordHistory) return;
        Array.Copy(_cpuHist, 1, _cpuHist, 0, HistoryLen - 1);
        Array.Copy(_memHist, 1, _memHist, 0, HistoryLen - 1);
        _cpuHist[HistoryLen - 1] = cpu;
        _memHist[HistoryLen - 1] = mem;
        if (_histCount < HistoryLen) _histCount++;
    }

    public void ClearMetrics() { Cpu = 0; Memory = 0; TreeCount = 0; }

    /// <summary>Builds sparkline geometry (only for the selected row).</summary>
    public void BuildSparklines(double w, double h)
    {
        CpuLine = Line(_cpuHist, w, h, Math.Max(100, Max(_cpuHist)));
        double mx = Max(_memHist);
        MemLine = Line(_memHist, w, h, mx * 1.3);
        MemPeak = Fmt.Bytes((long)mx);
    }

    private static double Max(double[] a) { double m = 0; foreach (var v in a) if (v > m) m = v; return m; }

    private PointCollection Line(double[] data, double w, double h, double max)
    {
        var pc = new PointCollection(HistoryLen + 3);
        if (max <= 0) max = 1;
        int n = Math.Max(_histCount, 1);
        int start = HistoryLen - n;
        double Y(double v) => h - Math.Min(1, v / max) * (h - 2) - 1;
        pc.Add(new Point(0, h));
        if (n == 1) { pc.Add(new Point(0, Y(data[HistoryLen - 1]))); pc.Add(new Point(w, Y(data[HistoryLen - 1]))); }
        else
        {
            double step = w / (n - 1);   // the samples collected so far span the full width
            for (int k = 0; k < n; k++) pc.Add(new Point(k * step, Y(data[start + k])));
        }
        pc.Add(new Point(w, h));
        pc.Freeze();
        return pc;
    }

    public void Tick() { if (IsOnline) Raise(nameof(UptimeText)); }
    public void ThemeChanged() { Raise(nameof(StatusBrush)); Raise(nameof(StatusSoftBrush)); }
}
