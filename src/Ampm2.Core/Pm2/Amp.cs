using System;
using System.Collections.Generic;
using System.Text;

namespace Ampm2.Pm2;

/// <summary>
/// AMP framing used by pm2's axon sockets.
/// Frame = 1 meta byte (version &lt;&lt; 4 | argc) then, per argument, a 4-byte big-endian length and the bytes.
/// amp-message prefixes each argument: "s:" string, "j:" JSON, anything else is a raw blob.
/// </summary>
public static class Amp
{
    public static byte[] Encode(IReadOnlyList<byte[]> args)
    {
        if (args.Count > 15) throw new ArgumentException("AMP supports at most 15 arguments.");
        int len = 1;
        foreach (var a in args) len += 4 + a.Length;
        var buf = new byte[len];
        int off = 0;
        buf[off++] = (byte)((1 << 4) | args.Count);
        foreach (var a in args)
        {
            buf[off++] = (byte)(a.Length >> 24);
            buf[off++] = (byte)(a.Length >> 16);
            buf[off++] = (byte)(a.Length >> 8);
            buf[off++] = (byte)a.Length;
            Buffer.BlockCopy(a, 0, buf, off, a.Length);
            off += a.Length;
        }
        return buf;
    }

    public static byte[] Str(string s) => Encoding.UTF8.GetBytes("s:" + s);
    public static byte[] Json(string json) => Encoding.UTF8.GetBytes("j:" + json);

    public static bool IsString(ReadOnlySpan<byte> a) => a.Length >= 2 && a[0] == (byte)'s' && a[1] == (byte)':';
    public static bool IsJson(ReadOnlySpan<byte> a) => a.Length >= 2 && a[0] == (byte)'j' && a[1] == (byte)':';

    public static string AsString(byte[] a) =>
        IsString(a) || IsJson(a) ? Encoding.UTF8.GetString(a, 2, a.Length - 2) : Encoding.UTF8.GetString(a);
}

/// <summary>Incremental AMP stream parser. Feed bytes, get whole frames (as argument lists).</summary>
public sealed class AmpParser
{
    private enum State { Meta, ArgLen, Arg }
    private State _state = State.Meta;
    private int _argc, _leni, _argLen, _argPos;
    private readonly byte[] _lenBuf = new byte[4];
    private byte[] _cur = Array.Empty<byte>();
    private List<byte[]> _args = new();

    public void Feed(ReadOnlySpan<byte> chunk, Action<List<byte[]>> onFrame)
    {
        int i = 0;
        while (i < chunk.Length)
        {
            switch (_state)
            {
                case State.Meta:
                    _argc = chunk[i++] & 0x0f;
                    _args = new List<byte[]>(_argc);
                    if (_argc == 0) { onFrame(_args); break; }
                    _state = State.ArgLen; _leni = 0;
                    break;
                case State.ArgLen:
                    _lenBuf[_leni++] = chunk[i++];
                    if (_leni == 4)
                    {
                        _argLen = (_lenBuf[0] << 24) | (_lenBuf[1] << 16) | (_lenBuf[2] << 8) | _lenBuf[3];
                        if (_argLen < 0 || _argLen > 256 * 1024 * 1024) throw new InvalidOperationException("Corrupt AMP frame.");
                        _cur = new byte[_argLen];
                        _argPos = 0;
                        _state = State.Arg;
                        if (_argLen == 0) CompleteArg(onFrame);
                    }
                    break;
                case State.Arg:
                    int take = Math.Min(_argLen - _argPos, chunk.Length - i);
                    chunk.Slice(i, take).CopyTo(_cur.AsSpan(_argPos));
                    _argPos += take; i += take;
                    if (_argPos == _argLen) CompleteArg(onFrame);
                    break;
            }
        }
    }

    private void CompleteArg(Action<List<byte[]>> onFrame)
    {
        _args.Add(_cur);
        if (_args.Count == _argc) { _state = State.Meta; onFrame(_args); }
        else { _state = State.ArgLen; _leni = 0; }
    }
}
