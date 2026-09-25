using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ampm2.Sys;

public static class Fmt
{
    public static string Bytes(long b)
    {
        if (b <= 0) return "—";
        double v = b;
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i >= 2 ? $"{v:0.0} {u[i]}" : $"{v:0} {u[i]}";
    }

    public static string Duration(TimeSpan t)
    {
        if (t.TotalSeconds < 0) return "—";
        if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds}s";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes}m";
        if (t.TotalHours < 24) return $"{(int)t.TotalHours}h {t.Minutes}m";
        return $"{(int)t.TotalDays}d {t.Hours}h";
    }

    public static string Unix(long ms) => ms <= 0 ? "—" : DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
}

public static class LogFiles
{
    /// <summary>Reads the last <paramref name="maxLines"/> lines of a log without locking it (pm2 keeps it open).</summary>
    public static List<string> Tail(string path, int maxLines, int maxBytes = 256 * 1024)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return lines;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, fs.Length - maxBytes);
            fs.Seek(start, SeekOrigin.Begin);
            var buf = new byte[fs.Length - start];
            int read = 0;
            while (read < buf.Length) { int n = fs.Read(buf, read, buf.Length - read); if (n <= 0) break; read += n; }
            var text = Encoding.UTF8.GetString(buf, 0, read);
            var all = text.Split('\n');
            int from = start > 0 ? 1 : 0;   // first line may be cut
            for (int i = Math.Max(from, all.Length - maxLines - 1); i < all.Length; i++)
            {
                var l = all[i].TrimEnd('\r');
                if (i == all.Length - 1 && l.Length == 0) continue;
                lines.Add(Ampm2.Pm2.Pm2Cli.StripAnsi(l));
            }
        }
        catch { }
        return lines;
    }
}
