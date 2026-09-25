using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Ampm2.Sys;

public readonly record struct ProcEntry(int Pid, int ParentPid, string Exe);
public readonly record struct TreeUsage(double CpuPercent, long MemoryBytes, int ProcessCount);

/// <summary>
/// Samples CPU and memory for pm2 apps natively (no WMI, no child processes).
/// Each app is measured as its whole process tree, because on Windows pm2 often launches
/// the real server as a grandchild (cmd.exe / bash.exe wrappers).
/// </summary>
/// <summary>Per-app CPU/memory, measured over each app's whole process tree.</summary>
public interface IProcessMetrics
{
    /// <summary>CPU is % of one core (like pm2 / top), averaged since the previous call.</summary>
    Dictionary<int, TreeUsage> Sample(IReadOnlyCollection<int> rootPids);
}

public static class ProcessMetricsFactory
{
    public static IProcessMetrics Create() => OperatingSystem.IsWindows() ? new ProcessMetrics() : new UnixProcessMetrics();
}

/// <summary>Windows implementation (Toolhelp snapshot + GetProcessTimes / GetProcessMemoryInfo).</summary>
public sealed class ProcessMetrics : IProcessMetrics
{
    private readonly Dictionary<int, long> _lastCpu = new();   // pid -> kernel+user 100ns
    private long _lastWall;
    private readonly int _cores = Environment.ProcessorCount;

    public static List<ProcEntry> Snapshot()
    {
        var list = new List<ProcEntry>(512);
        var snap = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;
        try
        {
            var e = new Native.PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<Native.PROCESSENTRY32W>() };
            if (Native.Process32FirstW(snap, ref e))
            {
                do list.Add(new ProcEntry((int)e.th32ProcessID, (int)e.th32ParentProcessID, e.szExeFile));
                while (Native.Process32NextW(snap, ref e));
            }
        }
        finally { Native.CloseHandle(snap); }
        return list;
    }

    /// <summary>Measures every root pid's tree. CPU is % of one core (like pm2 / top), averaged since the previous call.</summary>
    public Dictionary<int, TreeUsage> Sample(IReadOnlyCollection<int> rootPids)
    {
        var result = new Dictionary<int, TreeUsage>();
        if (rootPids.Count == 0) { _lastCpu.Clear(); return result; }

        var procs = Snapshot();
        var children = new Dictionary<int, List<int>>();
        var alive = new HashSet<int>();
        foreach (var p in procs)
        {
            alive.Add(p.Pid);
            if (p.Pid == p.ParentPid) continue;
            if (!children.TryGetValue(p.ParentPid, out var l)) children[p.ParentPid] = l = new List<int>();
            l.Add(p.Pid);
        }

        long now = DateTime.UtcNow.Ticks;
        double wall = _lastWall == 0 ? 0 : now - _lastWall;
        _lastWall = now;
        var seen = new HashSet<int>();

        foreach (var root in rootPids)
        {
            if (root <= 0 || !alive.Contains(root)) continue;
            double cpu = 0; long mem = 0; int count = 0;
            var stack = new Stack<int>();
            stack.Push(root);
            var visited = new HashSet<int>();
            while (stack.Count > 0)
            {
                int pid = stack.Pop();
                if (!visited.Add(pid) || visited.Count > 256) continue;
                count++;
                var (c, m) = Measure(pid, wall);
                cpu += c; mem += m;
                seen.Add(pid);
                if (children.TryGetValue(pid, out var kids))
                    foreach (var k in kids) stack.Push(k);
            }
            result[root] = new TreeUsage(Math.Round(cpu, 1), mem, count);
        }

        // forget pids that are gone
        var stale = new List<int>();
        foreach (var k in _lastCpu.Keys) if (!seen.Contains(k)) stale.Add(k);
        foreach (var k in stale) _lastCpu.Remove(k);
        return result;
    }

    private (double cpu, long mem) Measure(int pid, double wall)
    {
        var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return (0, 0);
        try
        {
            long mem = 0;
            if (Native.GetProcessMemoryInfo(h, out var pmc, (uint)Marshal.SizeOf<Native.PROCESS_MEMORY_COUNTERS>()))
                mem = (long)pmc.WorkingSetSize.ToUInt64();
            double cpu = 0;
            if (Native.GetProcessTimes(h, out _, out _, out var k, out var u))
            {
                long total = k + u;
                if (_lastCpu.TryGetValue(pid, out var prev) && wall > 0)
                    cpu = Math.Max(0, (total - prev) / wall * 100.0);
                _lastCpu[pid] = total;
            }
            return (cpu, mem);
        }
        finally { Native.CloseHandle(h); }
    }

    public int Cores => _cores;
}
