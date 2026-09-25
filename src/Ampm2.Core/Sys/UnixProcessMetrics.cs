using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Ampm2.Sys;

/// <summary>
/// macOS/Linux implementation: one `ps` snapshot per sample (pid, ppid, resident memory, cumulative CPU time),
/// summed over each app's process tree. CPU% comes from the CPU-time delta between samples, which is exact,
/// unlike ps's own %cpu (a decaying average on macOS).
/// </summary>
public sealed class UnixProcessMetrics : IProcessMetrics
{
    private readonly Dictionary<int, double> _lastCpuSec = new();
    private DateTime _lastWall;

    private readonly record struct Row(int Pid, int Ppid, long RssBytes, double CpuSec);

    public Dictionary<int, TreeUsage> Sample(IReadOnlyCollection<int> rootPids)
    {
        var result = new Dictionary<int, TreeUsage>();
        if (rootPids.Count == 0) { _lastCpuSec.Clear(); return result; }
        var rows = Snapshot();
        if (rows.Count == 0) return result;

        var byPid = new Dictionary<int, Row>(rows.Count);
        var children = new Dictionary<int, List<int>>();
        foreach (var r in rows)
        {
            byPid[r.Pid] = r;
            if (r.Pid == r.Ppid) continue;
            if (!children.TryGetValue(r.Ppid, out var l)) children[r.Ppid] = l = new List<int>();
            l.Add(r.Pid);
        }

        var now = DateTime.UtcNow;
        double wall = _lastWall == default ? 0 : (now - _lastWall).TotalSeconds;
        _lastWall = now;
        var seen = new HashSet<int>();

        foreach (var root in rootPids)
        {
            if (root <= 0 || !byPid.ContainsKey(root)) continue;
            double cpu = 0; long mem = 0; int count = 0;
            var stack = new Stack<int>();
            stack.Push(root);
            var visited = new HashSet<int>();
            while (stack.Count > 0)
            {
                int pid = stack.Pop();
                if (!visited.Add(pid) || visited.Count > 256 || !byPid.TryGetValue(pid, out var r)) continue;
                count++;
                mem += r.RssBytes;
                if (_lastCpuSec.TryGetValue(pid, out var prev) && wall > 0) cpu += Math.Max(0, (r.CpuSec - prev) / wall * 100.0);
                _lastCpuSec[pid] = r.CpuSec;
                seen.Add(pid);
                if (children.TryGetValue(pid, out var kids)) foreach (var k in kids) stack.Push(k);
            }
            result[root] = new TreeUsage(Math.Round(cpu, 1), mem, count);
        }

        var stale = new List<int>();
        foreach (var k in _lastCpuSec.Keys) if (!seen.Contains(k)) stale.Add(k);
        foreach (var k in stale) _lastCpuSec.Remove(k);
        return result;
    }

    private static List<Row> Snapshot()
    {
        var rows = new List<Row>(512);
        try
        {
            // "=" suppresses headers; rss is KiB; time is cumulative CPU ([[dd-]hh:]mm:ss[.cc])
            var psi = new ProcessStartInfo("/bin/ps")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add("-axo");
            psi.ArgumentList.Add("pid=,ppid=,rss=,time=");
            psi.Environment["LC_ALL"] = "C";
            using var p = Process.Start(psi);
            if (p == null) return rows;
            string? line;
            while ((line = p.StandardOutput.ReadLine()) != null)
            {
                var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 4) continue;
                if (int.TryParse(f[0], out var pid) && int.TryParse(f[1], out var ppid) && long.TryParse(f[2], out var rssKb))
                    rows.Add(new Row(pid, ppid, rssKb * 1024, ParseCpuTime(f[3])));
            }
            p.WaitForExit(2000);
        }
        catch { }
        return rows;
    }

    /// <summary>Parses ps "time": mm:ss.cc (macOS), [[dd-]hh:]mm:ss (Linux).</summary>
    public static double ParseCpuTime(string s)
    {
        double days = 0;
        int dash = s.IndexOf('-');
        if (dash > 0) { double.TryParse(s[..dash], NumberStyles.Float, CultureInfo.InvariantCulture, out days); s = s[(dash + 1)..]; }
        double total = 0;
        foreach (var part in s.Split(':'))
        {
            double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
            total = total * 60 + v;
        }
        return days * 86400 + total;
    }
}
