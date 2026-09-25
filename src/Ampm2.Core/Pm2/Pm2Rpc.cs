using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ampm2.Pm2;

/// <summary>
/// Client for pm2's daemon RPC (axon req socket + axon-rpc).
/// Request frame:  [ j:{"type":"call","method":M,"args":[...]} , s:&lt;msgId&gt; ]
/// Reply frame:    [ j:{"args":[...]} | j:{"error":...} , s:&lt;msgId&gt; ]
/// </summary>
public sealed class Pm2Rpc : IDisposable
{
    private readonly PipeConnection _conn;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonDocument>> _pending = new();
    private readonly string _identity = Environment.ProcessId.ToString();
    private long _seq;
    public event Action<Exception?>? Disconnected;
    public int DaemonPid => _conn.ServerProcessId;

    private Pm2Rpc(PipeConnection conn)
    {
        _conn = conn;
        _conn.Frame += OnFrame;
        _conn.Closed += OnClosed;
        _conn.StartReading();
    }

    public static async Task<Pm2Rpc> ConnectAsync(string pipeName, CancellationToken ct) =>
        new(await PipeConnection.ConnectAsync(pipeName, 1500, ct).ConfigureAwait(false));

    private void OnFrame(List<byte[]> args)
    {
        if (args.Count < 2) return;
        var id = Amp.AsString(args[^1]);
        if (!_pending.TryRemove(id, out var tcs)) return;
        try
        {
            var a = args[0];
            var doc = Amp.IsJson(a) ? JsonDocument.Parse(a.AsMemory(2)) : JsonDocument.Parse("null");
            tcs.TrySetResult(doc);
        }
        catch (Exception ex) { tcs.TrySetException(ex); }
    }

    private void OnClosed(Exception? ex)
    {
        foreach (var kv in _pending)
            if (_pending.TryRemove(kv.Key, out var t)) t.TrySetException(new Exception("Connection to pm2 closed."));
        Disconnected?.Invoke(ex);
    }

    /// <summary>Calls a daemon method. argsJson is the JSON array of arguments. Returns the reply document ({"args":[...]}).</summary>
    public async Task<JsonDocument> CallAsync(string method, string argsJson, TimeSpan timeout, CancellationToken ct = default)
    {
        var id = _identity + ":" + Interlocked.Increment(ref _seq);
        var tcs = new TaskCompletionSource<JsonDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        var body = "{\"type\":\"call\",\"method\":" + JsonSerializer.Serialize(method) + ",\"args\":" + argsJson + "}";
        try
        {
            await _conn.SendAsync(new[] { Amp.Json(body), Amp.Str(id) }, ct).ConfigureAwait(false);
            using var reg = ct.Register(() => tcs.TrySetCanceled());
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout, CancellationToken.None)).ConfigureAwait(false);
            if (done != tcs.Task) throw new TimeoutException($"pm2 did not answer '{method}' in time.");
            var doc = await tcs.Task.ConfigureAwait(false);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var err))
            {
                var msg = err.ValueKind == JsonValueKind.String ? err.GetString() : err.ToString();
                doc.Dispose();
                throw new Pm2Exception(msg ?? "pm2 error");
            }
            return doc;
        }
        finally { _pending.TryRemove(id, out _); }
    }

    public async Task<List<Pm2Process>> GetProcessesAsync(CancellationToken ct = default)
    {
        using var doc = await CallAsync("getMonitorData", "[{}]", TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array && args.GetArrayLength() > 0)
            return Pm2Process.ParseList(args[0]);
        return new List<Pm2Process>();
    }

    public async Task<string> GetVersionAsync()
    {
        try
        {
            using var doc = await CallAsync("getVersion", "[{}]", TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            return doc.RootElement.TryGetProperty("args", out var a) && a.GetArrayLength() > 0 ? a[0].ToString() : "";
        }
        catch { return ""; }
    }

    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(90);
    public async Task StartAsync(int id) => (await CallAsync("startProcessId", $"[{id}]", ActionTimeout).ConfigureAwait(false)).Dispose();
    public async Task StopAsync(int id) => (await CallAsync("stopProcessId", $"[{id}]", ActionTimeout).ConfigureAwait(false)).Dispose();
    public async Task RestartAsync(int id) => (await CallAsync("restartProcessId", $"[{{\"id\":{id}}}]", ActionTimeout).ConfigureAwait(false)).Dispose();
    public async Task ReloadAsync(int id) => (await CallAsync("reloadProcessId", $"[{{\"id\":{id}}}]", ActionTimeout).ConfigureAwait(false)).Dispose();
    public async Task DeleteAsync(int id) => (await CallAsync("deleteProcessId", $"[{id}]", ActionTimeout).ConfigureAwait(false)).Dispose();
    public async Task ResetAsync(int id) => (await CallAsync("resetMetaProcessId", $"[{id}]", ActionTimeout).ConfigureAwait(false)).Dispose();

    public void Dispose() => _conn.Dispose();
}

public sealed class Pm2Exception : Exception
{
    public Pm2Exception(string message) : base(message) { }
}
