using System.Collections.Concurrent;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// An append-only log of trace records kept in a file of the machine, not in memory. The statement
/// trace used to hold every record of a run in queues (millions over one attack, most of the
/// memory of the gate process: 16 to 19 GB resident, a host that swapped). Every record is still
/// kept, in order, and read back whole when a gate judges it: only where it lives has changed.
/// Positions are the count of records, as the queues' were. Hot texts (a tenant, an endpoint, a
/// statement sent for every request) are written once and referred to by number, and read back as
/// one shared string instance.
/// </summary>
public sealed class SpillLog<T>
{
    private const int IndexEvery = 1024;
    private const int BufferSize = 1 << 20;

    private readonly Action<SpillWriter, T> _write;
    private readonly Func<SpillReader, T> _read;
    private readonly Lock _gate = new();
    private readonly List<long> _offsets = [];
    private Microsoft.Win32.SafeHandles.SafeFileHandle? _file;
    private byte[]? _pending;
    private int _pendingLength;
    private long _flushed;
    private int _count;

    [ThreadStatic] private static MemoryStream? _frame;
    [ThreadStatic] private static SpillWriter? _writer;

    public SpillLog(Action<SpillWriter, T> write, Func<SpillReader, T> read)
    {
        _write = write;
        _read = read;
    }

    /// <summary>The records added so far.</summary>
    public int Count => Volatile.Read(ref _count);

    public void Add(T item)
    {
        var frame = _frame ??= new MemoryStream(4096);
        frame.SetLength(0);
        var writer = _writer ??= new SpillWriter(frame);
        _write(writer, item);
        writer.Flush();
        var length = (int)frame.Length;
        lock (_gate)
        {
            if (_file is null)
            {
                // The file is unlinked at once: the open handle keeps it, nothing is left behind when the process ends.
                var path = Path.Combine(Path.GetTempPath(), $"erp-gate-trace-{Environment.ProcessId}-{Guid.NewGuid():N}.bin");
                _file = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // a platform that cannot unlink an open file keeps it until the machine's temp folder is cleaned
                }
                _pending = new byte[BufferSize];
            }
            if (_count % IndexEvery == 0)
            {
                _offsets.Add(_flushed + _pendingLength);
            }
            if (_pendingLength + 4 + length > _pending!.Length)
            {
                Flush();
            }
            if (4 + length > _pending.Length)
            {
                // a record larger than the buffer goes straight to the file
                var big = new byte[4 + length];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(big, length);
                frame.GetBuffer().AsSpan(0, length).CopyTo(big.AsSpan(4));
                RandomAccess.Write(_file, big, _flushed);
                _flushed += big.Length;
            }
            else
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(_pending.AsSpan(_pendingLength), length);
                frame.GetBuffer().AsSpan(0, length).CopyTo(_pending.AsSpan(_pendingLength + 4));
                _pendingLength += 4 + length;
            }
            Volatile.Write(ref _count, _count + 1);
        }
    }

    private void Flush()
    {
        if (_pendingLength > 0)
        {
            RandomAccess.Write(_file!, _pending.AsSpan(0, _pendingLength), _flushed);
            _flushed += _pendingLength;
            _pendingLength = 0;
        }
    }

    /// <summary>The records from position <paramref name="since"/> on, as they were added.</summary>
    public IEnumerable<T> From(int since)
    {
        long start;
        int end;
        lock (_gate)
        {
            end = _count;
            if (since >= end)
            {
                return [];
            }
            Flush();
            start = _offsets[since / IndexEvery];
        }
        return Read(_file!, start, since - since / IndexEvery * IndexEvery, end - since / IndexEvery * IndexEvery);
    }

    private IEnumerable<T> Read(Microsoft.Win32.SafeHandles.SafeFileHandle file, long start, int skip, int total)
    {
        var buffer = new byte[BufferSize];
        var have = 0;
        var at = 0;
        var position = start;
        bool Need(int n)
        {
            while (have - at < n)
            {
                if (at > 0)
                {
                    Buffer.BlockCopy(buffer, at, buffer, 0, have - at);
                    have -= at;
                    at = 0;
                }
                if (buffer.Length < n)
                {
                    Array.Resize(ref buffer, Math.Max(n, buffer.Length * 2));
                }
                var read = RandomAccess.Read(file, buffer.AsSpan(have), position);
                if (read == 0)
                {
                    return false;
                }
                have += read;
                position += read;
            }
            return true;
        }
        // Texts that are not numbered (a request's path and trace id) come back once per distinct
        // value for the whole read, not once per record that names them.
        var shared = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < total; i++)
        {
            if (!Need(4))
            {
                throw new EndOfStreamException("The trace file ended inside a record.");
            }
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(at));
            if (!Need(4 + length))
            {
                throw new EndOfStreamException("The trace file ended inside a record.");
            }
            if (i >= skip)
            {
                using var frame = new MemoryStream(buffer, at + 4, length, writable: false);
                yield return _read(new SpillReader(frame, shared));
            }
            at += 4 + length;
        }
    }
}

/// <summary>Writes the fields of one record: texts (hot ones by number), numbers, flags and the
/// application (the module catalogue) that ran the statement.</summary>
public sealed class SpillWriter(Stream stream) : BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)
{
    public void Text(string? text)
    {
        if (text is null)
        {
            Write7BitEncodedInt(0x7fffffff);
            return;
        }
        var id = SpillStrings.Id(text);
        Write7BitEncodedInt(id >= 0 ? id + 1 : 0);
        if (id < 0)
        {
            Write(text);
        }
    }

    public void Flag(bool? flag) => Write((byte)(flag is null ? 2 : flag.Value ? 1 : 0));

    public void App(object? app) => Write7BitEncodedInt(SpillStrings.AppId(app));
}

public sealed class SpillReader(Stream stream, Dictionary<string, string>? shared = null) : BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true)
{
    public string? Text()
    {
        var tag = Read7BitEncodedInt();
        if (tag == 0x7fffffff)
        {
            return null;
        }
        if (tag != 0)
        {
            return SpillStrings.Get(tag - 1);
        }
        var text = ReadString();
        if (shared is null || text.Length > 256)
        {
            return text;
        }
        if (shared.TryGetValue(text, out var known))
        {
            return known;
        }
        shared[text] = text;
        return text;
    }

    public string RequiredText() => Text() ?? "";

    public bool? Flag() => ReadByte() switch { 2 => null, 1 => true, _ => false };

    public object? App() => SpillStrings.AppOf(Read7BitEncodedInt());
}

/// <summary>The texts and applications the trace logs refer to by number.</summary>
internal static class SpillStrings
{
    private const int MaxLength = 8192;
    private const long MaxChars = 48_000_000;
    private const int MaxEntries = 400_000;

    private static readonly ConcurrentDictionary<string, int> Ids = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<int, string> Texts = new();
    private static readonly Dictionary<object, int> AppIds = new(ReferenceEqualityComparer.Instance);
    private static readonly List<object> Apps = [];
    private static readonly Lock Gate = new();
    private static long _chars;

    /// <summary>The number of the text, giving it one while there is room; -1 when it has none.</summary>
    public static int Id(string text)
    {
        if (Ids.TryGetValue(text, out var known))
        {
            return known;
        }
        if (text.Length > MaxLength)
        {
            return -1;
        }
        lock (Gate)
        {
            if (Ids.TryGetValue(text, out known))
            {
                return known;
            }
            if (Ids.Count >= MaxEntries || _chars + text.Length > MaxChars)
            {
                return -1;
            }
            var id = Ids.Count;
            _chars += text.Length;
            Texts[id] = text;
            Ids[text] = id;
            return id;
        }
    }

    public static string Get(int id) => Texts[id];

    public static int AppId(object? app)
    {
        if (app is null)
        {
            return 0;
        }
        lock (Gate)
        {
            if (!AppIds.TryGetValue(app, out var id))
            {
                Apps.Add(app);
                id = Apps.Count;
                AppIds[app] = id;
            }
            return id;
        }
    }

    public static object? AppOf(int id)
    {
        if (id == 0)
        {
            return null;
        }
        lock (Gate)
        {
            return Apps[id - 1];
        }
    }
}
