using System.Text;

namespace Erp.Modules.Identity.Auth.Passkeys;

/// <summary>
/// Reads the small CBOR (RFC 8949) documents WebAuthn sends: the attestation object (a map with
/// text keys) and the COSE public key inside it (a map with integer keys). Only definite lengths,
/// integers, byte and text strings, arrays, maps and the simple values false, true and null are
/// accepted, with limits on nesting and size; anything else (tags, floats, indefinite lengths,
/// duplicate keys, trailing bytes where none are expected) is refused as malformed. No package:
/// the subset is small and a refusal is always safe here.
/// </summary>
internal sealed class Cbor
{
    private const int MaxDepth = 8;
    private const int MaxItems = 256;

    private readonly byte[] _data;
    private int _position;

    private Cbor(byte[] data, int position)
    {
        _data = data;
        _position = position;
    }

    /// <summary>Reads one item starting at <paramref name="offset"/>; <paramref name="end"/> is
    /// where it ended. Null when the bytes are not one well-formed item of the accepted subset.</summary>
    public static bool TryRead(byte[] data, int offset, out object? item, out int end)
    {
        item = null;
        end = offset;
        if (offset < 0 || offset > data.Length)
        {
            return false;
        }
        var reader = new Cbor(data, offset);
        try
        {
            item = reader.Read(0);
            end = reader._position;
            return true;
        }
        catch (FormatException)
        {
            item = null;
            return false;
        }
    }

    /// <summary>The whole of <paramref name="data"/> as one item (no bytes after it).</summary>
    public static bool TryReadWhole(byte[] data, out object? item)
    {
        if (TryRead(data, 0, out item, out var end) && end == data.Length)
        {
            return true;
        }
        item = null;
        return false;
    }

    private object? Read(int depth)
    {
        if (depth > MaxDepth)
        {
            throw new FormatException("CBOR nested too deeply");
        }
        var initial = Next();
        var major = initial >> 5;
        var info = initial & 0x1f;
        if (major == 7)
        {
            return info switch
            {
                20 => false,
                21 => true,
                22 => null,
                _ => throw new FormatException("unsupported CBOR simple value"),
            };
        }
        var argument = Argument(info);
        switch (major)
        {
            case 0:
                return argument <= long.MaxValue ? (long)argument : throw new FormatException("CBOR integer too large");
            case 1:
                return argument <= long.MaxValue ? -1 - (long)argument : throw new FormatException("CBOR integer too large");
            case 2:
                return Bytes(argument);
            case 3:
                try
                {
                    return new UTF8Encoding(false, true).GetString(Bytes(argument));
                }
                catch (DecoderFallbackException)
                {
                    throw new FormatException("CBOR text is not UTF-8");
                }
            case 4:
            {
                var count = Count(argument);
                var list = new List<object?>(count);
                for (var i = 0; i < count; i++)
                {
                    list.Add(Read(depth + 1));
                }
                return list;
            }
            case 5:
            {
                var count = Count(argument);
                var map = new Dictionary<object, object?>(count);
                for (var i = 0; i < count; i++)
                {
                    var key = Read(depth + 1) switch
                    {
                        long n => (object)n,
                        string t => t,
                        _ => throw new FormatException("CBOR map key is not an integer or text"),
                    };
                    if (!map.TryAdd(key, Read(depth + 1)))
                    {
                        throw new FormatException("CBOR map key repeated");
                    }
                }
                return map;
            }
            default:
                throw new FormatException("unsupported CBOR major type");
        }
    }

    private byte Next() => _position < _data.Length ? _data[_position++] : throw new FormatException("CBOR ends early");

    private ulong Argument(int info)
    {
        if (info < 24)
        {
            return (ulong)info;
        }
        var size = info switch
        {
            24 => 1,
            25 => 2,
            26 => 4,
            27 => 8,
            _ => throw new FormatException("indefinite or reserved CBOR length"),
        };
        ulong value = 0;
        for (var i = 0; i < size; i++)
        {
            value = (value << 8) | Next();
        }
        return value;
    }

    private int Count(ulong argument) => argument <= MaxItems ? (int)argument : throw new FormatException("CBOR container too large");

    private byte[] Bytes(ulong length)
    {
        if (length > (ulong)(_data.Length - _position))
        {
            throw new FormatException("CBOR string runs past the end");
        }
        var bytes = _data.AsSpan(_position, (int)length).ToArray();
        _position += (int)length;
        return bytes;
    }
}
