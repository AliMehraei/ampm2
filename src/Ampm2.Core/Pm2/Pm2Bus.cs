using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ampm2.Pm2;

public readonly record struct LogEvent(int ProcessId, bool IsError, string Text, DateTime At);

/// <summary>
/// Subscribes to pm2's event bus (axon pub socket). Frames are [ s:&lt;event&gt;, j:&lt;data&gt; ].
/// Only the topic is decoded for every frame; log payloads are parsed only while a log view is open.
/// </summary>
public sealed class Pm2Bus : IDisposable
{
    private readonly PipeConnection _conn;
    public event Action<string, int>? ProcessEvent;      // (event, pm_id)
    public event Action<LogEvent>? Log;
    public event Action<Exception?>? Disconnected;
    /// <summary>pm_id whose log lines should be decoded; -1 = none.</summary>
    public volatile int LogFilter = -1;

    private Pm2Bus(PipeConnection conn)
    {
        _conn = conn;
        _conn.Frame += OnFrame;
        _conn.Closed += e => Disconnected?.Invoke(e);
        _conn.StartReading();
    }

    public static async Task<Pm2Bus> ConnectAsync(string pipeName, CancellationToken ct) =>
        new(await PipeConnection.ConnectAsync(pipeName, 1500, ct).ConfigureAwait(false));

    private void OnFrame(List<byte[]> args)
    {
        if (args.Count < 2 || !Amp.IsString(args[0])) return;
        var topic = Amp.AsString(args[0]);
        bool isLog = topic == "log:out" || topic == "log:err";
        if (isLog && LogFilter == -1) return;
        if (!isLog && topic != "process:event") return;
        if (!Amp.IsJson(args[1])) return;
        try
        {
            using var doc = JsonDocument.Parse(args[1].AsMemory(2));
            var root = doc.RootElement;
            int pmId = -1;
            if (root.TryGetProperty("process", out var p) && p.ValueKind == JsonValueKind.Object &&
                p.TryGetProperty("pm_id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                pmId = idEl.GetInt32();
            if (isLog)
            {
                if (LogFilter != pmId) return;
                var text = root.TryGetProperty("data", out var d) ? (d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : d.ToString()) : "";
                var at = root.TryGetProperty("at", out var atEl) && atEl.ValueKind == JsonValueKind.Number && atEl.TryGetInt64(out var ms)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime : DateTime.Now;
                Log?.Invoke(new LogEvent(pmId, topic == "log:err", text, at));
            }
            else
            {
                var ev = root.TryGetProperty("event", out var evEl) && evEl.ValueKind == JsonValueKind.String ? evEl.GetString() ?? topic : topic;
                ProcessEvent?.Invoke(ev, pmId);
            }
        }
        catch { /* ignore malformed bus traffic */ }
    }

    public void Dispose() => _conn.Dispose();
}
