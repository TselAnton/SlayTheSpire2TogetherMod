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
        if (_complete || now - _started > 1500 || _parts.Length != fragment.Count || _parts[fragment.Index] != null) return null;
        _parts[fragment.Index] = fragment.Data;
        if (_parts.Any(p => p == null)) return null;
        _complete = true;
        int size = _parts.Sum(p => p!.Length);
        if (size > DemoScreenMessage.MaxFrameBytes) return null;
        var result = new byte[size]; int offset = 0;
        foreach (var part in _parts) { part!.CopyTo(result, offset); offset += part.Length; }
        _parts = []; return result;
    }

    // Check JPEG dimensions BEFORE native decoding; do not trust compressed size.
    public static bool SafeJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length is < 16 or > DemoScreenMessage.MaxFrameBytes || data[0] != 0xff || data[1] != 0xd8) return false;
        int pos = 2;
        while (pos + 4 <= data.Length)
        {
            if (data[pos++] != 0xff) return false;
            while (pos < data.Length && data[pos] == 0xff) pos++;
            if (pos >= data.Length) return false;
            byte marker = data[pos++];
            if (marker is 0xda or 0xd9) return false;
            if (marker is 0x01 or >= 0xd0 and <= 0xd7) continue;
            if (pos + 2 > data.Length) return false;
            int length = (data[pos] << 8) | data[pos + 1];
            if (length < 2 || pos + length > data.Length) return false;
            if (marker is 0xc0 or 0xc1 or 0xc2)
            {
                if (length < 8) return false;
                int height = (data[pos + 3] << 8) | data[pos + 4];
                int width = (data[pos + 5] << 8) | data[pos + 6];
                return width is > 0 and <= 1280 && height is > 0 and <= 720;
            }
            pos += length;
        }
        return false;
    }
}
