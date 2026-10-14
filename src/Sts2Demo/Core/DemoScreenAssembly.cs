using Sts2Demo.Networking;

namespace Sts2Demo.Core;

/// <summary>One bounded in-flight frame per observed peer. No disk, decompression or unbounded queues.</summary>
public sealed class DemoScreenAssembly
{
    private uint? _sequence;
    private byte[]?[] _parts = [];
    private ulong _started;
    private bool _complete;
    public byte[]? Accept(DemoScreenMessage fragment, ulong now)
    {
        if (fragment.Data == null || fragment.Data.Length is <= 0 or > DemoScreenMessage.ChunkBytes
            || fragment.Count is 0 or > DemoScreenMessage.MaxChunks || fragment.Index >= fragment.Count) return null;
        if (_sequence != fragment.Sequence)
        {
            if (_sequence is uint previous && !DemoSession.Newer(fragment.Sequence, previous)) return null;
            _sequence = fragment.Sequence; _started = now; _complete = false;
            _parts = new byte[fragment.Count][];
        }
        if (_complete || now - _started > 5000 || _parts.Length != fragment.Count || _parts[fragment.Index] != null) return null;
        _parts[fragment.Index] = fragment.Data;
        if (_parts.Any(p => p == null)) return null;
        _complete = true;
        int size = _parts.Sum(p => p!.Length);
        if (size > DemoScreenMessage.MaxFrameBytes) return null;
        var result = new byte[size]; int offset = 0;
        foreach (var part in _parts) { part!.CopyTo(result, offset); offset += part.Length; }
        _parts = []; return result;
    }

    public static bool SafeMetadata(byte[] data)
    {
        try { return data.Length <= DemoScreenMessage.MaxFrameBytes && DemoVisualCodec.Valid(DemoVisualCodec.Decode(data)); }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or IOException or InvalidOperationException) { return false; }
    }
}
