using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Ampm2.Pm2;

public enum ConnectFailure { None, NotRunning, AccessDenied, Other }

public sealed class PipeConnectException : Exception
{
    public ConnectFailure Kind { get; }
    public PipeConnectException(ConnectFailure kind, string message, Exception? inner = null) : base(message, inner) => Kind = kind;
}

/// <summary>
/// A connected axon peer: writes AMP frames, reads frames on a background loop.
/// Windows: a named pipe (endpoint = pipe name). macOS/Linux: a Unix domain socket (endpoint = socket path).
/// </summary>
public sealed class PipeConnection : IDisposable
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    public event Action<List<byte[]>>? Frame;
    public event Action<Exception?>? Closed;
    /// <summary>pid of the daemon on the other end (0 when the OS cannot tell).</summary>
    public int ServerProcessId { get; }

    private PipeConnection(Stream stream, int serverPid)
    {
        _stream = stream;
        ServerProcessId = serverPid;
    }

    public static Task<PipeConnection> ConnectAsync(string endpoint, int timeoutMs, CancellationToken ct) =>
        OperatingSystem.IsWindows() ? ConnectPipeAsync(endpoint, timeoutMs, ct) : ConnectUnixAsync(endpoint, timeoutMs, ct);

    private static async Task<PipeConnection> ConnectPipeAsync(string pipeName, int timeoutMs, CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeoutMs, ct).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            pipe.Dispose();
            throw new PipeConnectException(ConnectFailure.AccessDenied, "Access to the pm2 pipe was denied.", ex);
        }
        catch (TimeoutException ex)
        {
            pipe.Dispose();
            throw new PipeConnectException(ConnectFailure.NotRunning, "The pm2 daemon is not running.", ex);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            pipe.Dispose();
            throw new PipeConnectException(ConnectFailure.Other, ex.Message, ex);
        }
        int pid = OperatingSystem.IsWindows() && GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var p) ? (int)p : 0;
        return new PipeConnection(pipe, pid);
    }

    private static async Task<PipeConnection> ConnectUnixAsync(string path, int timeoutMs, CancellationToken ct)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), timeout.Token).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            socket.Dispose();
            throw ex.SocketErrorCode switch
            {
                SocketError.AccessDenied => new PipeConnectException(ConnectFailure.AccessDenied, "Access to the pm2 socket was denied.", ex),
                // no socket file, or a stale one left by a daemon that died
                SocketError.ConnectionRefused or SocketError.AddressNotAvailable or SocketError.HostNotFound or SocketError.NotConnected
                    => new PipeConnectException(ConnectFailure.NotRunning, "The pm2 daemon is not running.", ex),
                _ => new PipeConnectException(ConnectFailure.Other, ex.Message, ex),
            };
        }
        catch (OperationCanceledException ex)
        {
            socket.Dispose();
            throw new PipeConnectException(ConnectFailure.NotRunning, "The pm2 daemon did not answer.", ex);
        }
        return new PipeConnection(new NetworkStream(socket, ownsSocket: true), PeerPid(socket));
    }

    /// <summary>Peer pid of a connected Unix socket: LOCAL_PEERPID on macOS, SO_PEERCRED on Linux.</summary>
    private static int PeerPid(Socket s)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                Span<byte> buf = stackalloc byte[4];
                int n = s.GetRawSocketOption(0 /* SOL_LOCAL */, 2 /* LOCAL_PEERPID */, buf);
                return n >= 4 ? BitConverter.ToInt32(buf) : 0;
            }
            if (OperatingSystem.IsLinux())
            {
                Span<byte> buf = stackalloc byte[12];   // struct ucred { pid_t pid; uid_t uid; gid_t gid; }
                int n = s.GetRawSocketOption(1 /* SOL_SOCKET */, 17 /* SO_PEERCRED */, buf);
                return n >= 4 ? BitConverter.ToInt32(buf) : 0;
            }
        }
        catch { }
        return 0;
    }

    /// <summary>Cheap existence check before connecting (named pipe listing on Windows, socket file elsewhere).</summary>
    public static bool PipeExists(string endpoint)
    {
        if (!OperatingSystem.IsWindows()) return File.Exists(endpoint);
        try
        {
            foreach (var p in Directory.EnumerateFiles(@"\\.\pipe\"))
                if (string.Equals(Path.GetFileName(p), endpoint, StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch { return true; } // cannot enumerate: let the connect attempt decide
        return false;
    }

    public void StartReading() => _ = ReadLoop();

    private async Task ReadLoop()
    {
        var parser = new AmpParser();
        var buf = new byte[64 * 1024];
        Exception? error = null;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int n = await _stream.ReadAsync(buf.AsMemory(), _cts.Token).ConfigureAwait(false);
                if (n <= 0) break;
                parser.Feed(buf.AsSpan(0, n), f => Frame?.Invoke(f));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { error = ex; }
        Closed?.Invoke(error);
    }

    public async Task SendAsync(IReadOnlyList<byte[]> args, CancellationToken ct)
    {
        var bytes = Amp.Encode(args);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally { _writeLock.Release(); }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _stream.Dispose(); } catch { }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverProcessId);
}
