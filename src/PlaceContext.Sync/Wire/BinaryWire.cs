using System.Text;

namespace PlaceContext.Sync.Wire;

/// <summary>Append-only writer for the PCSP primitive encodings (see docs/SYNC-PROTOCOL.md §4).</summary>
public sealed class WireWriter
{
    private readonly List<byte> _buffer = new();

    public void UVarint(long value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "uvarint must be non-negative.");
        var v = (ulong)value;
        while (v >= 0x80)
        {
            _buffer.Add((byte)(v | 0x80));
            v >>= 7;
        }
        _buffer.Add((byte)v);
    }

    public void U8(byte value) => _buffer.Add(value);

    public void Bytes(ReadOnlySpan<byte> value)
    {
        UVarint(value.Length);
        foreach (var b in value) _buffer.Add(b);
    }

    public void String(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        Bytes(bytes);
    }

    public void Guid(Guid value) => _buffer.AddRange(value.ToByteArray(bigEndian: true));

    public byte[] ToArray() => _buffer.ToArray();
}

/// <summary>Forward cursor over a PCSP byte span. Throws <see cref="WireFormatException"/> on truncation.</summary>
public ref struct WireReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _pos;

    public WireReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _pos = 0;
    }

    public readonly int Position => _pos;
    public readonly bool End => _pos >= _data.Length;

    public long UVarint()
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            if (_pos >= _data.Length) throw new WireFormatException("Truncated uvarint.");
            if (shift > 63) throw new WireFormatException("uvarint overflow.");
            byte b = _data[_pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return (long)result;
    }

    public byte U8()
    {
        if (_pos >= _data.Length) throw new WireFormatException("Truncated byte.");
        return _data[_pos++];
    }

    public byte[] Bytes()
    {
        long len = UVarint();
        if (len < 0 || _pos + len > _data.Length) throw new WireFormatException("Truncated bytes.");
        var slice = _data.Slice(_pos, (int)len).ToArray();
        _pos += (int)len;
        return slice;
    }

    public string String() => Encoding.UTF8.GetString(Bytes());

    public Guid Guid()
    {
        if (_pos + 16 > _data.Length) throw new WireFormatException("Truncated guid.");
        var g = new Guid(_data.Slice(_pos, 16), bigEndian: true);
        _pos += 16;
        return g;
    }
}

/// <summary>Raised when a byte stream is not valid PCSP (truncated, overflowing, or unknown kind).</summary>
public sealed class WireFormatException : Exception
{
    public WireFormatException(string message) : base(message) { }
}
