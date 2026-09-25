using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Ampm2.Sys;

public readonly record struct StrayDaemon(int Pid, DateTime Started, long MemoryBytes);

/// <summary>
/// On Windows every pm2 command run WITHOUT access to the daemon's pipe (typically: a normal terminal
/// while the daemon was started from an elevated one) spawns a fresh "Daemon.js" that can never bind
/// the pipe and never exits. They hold ~50 MB each and manage nothing.
/// A stray = a node process running pm2\lib\Daemon.js that is NOT the pipe's server and has no children.
/// </summary>
public static class StrayDaemons
{
    public static List<StrayDaemon> Find(int realDaemonPid)
    {
        var result = new List<StrayDaemon>();
        if (realDaemonPid <= 0) return result;   // never guess which daemon is real

        var snap = ProcessMetrics.Snapshot();
        var parents = new HashSet<int>();
        foreach (var p in snap) parents.Add(p.ParentPid);

        foreach (var p in snap)
        {
            if (!p.Exe.Equals("node.exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Pid == realDaemonPid || parents.Contains(p.Pid)) continue;
            var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Pid);
            if (h == IntPtr.Zero) continue;
            try
            {
                var cmd = CommandLine(h);
                if (cmd == null) continue;
                if (cmd.IndexOf(@"pm2\lib\Daemon.js", StringComparison.OrdinalIgnoreCase) < 0 &&
                    cmd.IndexOf("pm2/lib/Daemon.js", StringComparison.OrdinalIgnoreCase) < 0) continue;
                DateTime started = DateTime.MinValue;
                if (Native.GetProcessTimes(h, out var c, out _, out _, out _)) started = DateTime.FromFileTime(c);
                long ws = 0;
                if (Native.GetProcessMemoryInfo(h, out var pmc, (uint)Marshal.SizeOf<Native.PROCESS_MEMORY_COUNTERS>()))
                    ws = (long)pmc.WorkingSetSize.ToUInt64();
                result.Add(new StrayDaemon(p.Pid, started, ws));
            }
            finally { Native.CloseHandle(h); }
        }
        result.Sort((a, b) => a.Started.CompareTo(b.Started));
        return result;
    }

    public static int Kill(IEnumerable<StrayDaemon> strays, int realDaemonPid)
    {
        int n = 0;
        foreach (var s in strays)
        {
            if (s.Pid == realDaemonPid) continue;
            var h = Native.OpenProcess(Native.PROCESS_TERMINATE, false, s.Pid);
            if (h == IntPtr.Zero) continue;
            try { if (Native.TerminateProcess(h, 1)) n++; }
            finally { Native.CloseHandle(h); }
        }
        return n;
    }

    // ProcessCommandLineInformation (60) returns a UNICODE_STRING followed by its buffer. Works with limited access.
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr h, int cls, IntPtr buf, int len, out int retLen);

    private static string? CommandLine(IntPtr h)
    {
        int len = 4096;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var buf = Marshal.AllocHGlobal(len);
            try
            {
                int st = NtQueryInformationProcess(h, 60, buf, len, out int needed);
                if (st == 0)
                {
                    ushort bytes = (ushort)Marshal.ReadInt16(buf);
                    IntPtr str = Marshal.ReadIntPtr(buf, IntPtr.Size);   // UNICODE_STRING.Buffer (aligned)
                    return bytes == 0 || str == IntPtr.Zero ? "" : Marshal.PtrToStringUni(str, bytes / 2);
                }
                if (needed <= len) return null;
                len = needed;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return null;
    }
}
