using System.Buffers.Binary;
using System.Text;

namespace Vakage.Core.Sources.WhatsApp;

/// <summary>
/// Reads Apple's binary property lists (<c>bplist00</c>), enough for the
/// NSKeyedArchiver blobs WhatsApp keeps beside a message on an iPhone
/// (<c>ZWAMEDIAITEM.ZMETADATA</c>): which message it replies to, a link's
/// preview, how often it was forwarded. Values come back as null, bool, long,
/// double, string, byte[], <see cref="Uid"/>, List&lt;object?&gt; or
/// Dictionary&lt;string, object?&gt;.
/// </summary>
public static class BinaryPlist
{
    public readonly record struct Uid(int Value);

    public static object? Parse(byte[] data)
    {
        if (data.Length < 40 || Encoding.ASCII.GetString(data, 0, 8) != "bplist00")
            throw new FormatException("Not a binary property list.");
        var trailer = data.AsSpan(data.Length - 32);
        int offsetSize = trailer[6], refSize = trailer[7];
        var count = (int)BinaryPrimitives.ReadUInt64BigEndian(trailer[8..]);
        var top = (int)BinaryPrimitives.ReadUInt64BigEndian(trailer[16..]);
        var tableAt = (int)BinaryPrimitives.ReadUInt64BigEndian(trailer[24..]);
        var offsets = new int[count];
        for (var i = 0; i < count; i++) offsets[i] = (int)ReadUInt(data, tableAt + i * offsetSize, offsetSize);
        return new Reader(data, offsets, refSize).Read(top, 0);
    }

    private static ulong ReadUInt(byte[] d, int at, int size)
    {
        ulong v = 0;
        for (var i = 0; i < size; i++) v = (v << 8) | d[at + i];
        return v;
    }

    private sealed class Reader(byte[] d, int[] offsets, int refSize)
    {
        public object? Read(int index, int depth)
        {
            if (depth > 64 || index < 0 || index >= offsets.Length) return null;
            var at = offsets[index];
            var marker = d[at];
            int kind = marker >> 4, info = marker & 0x0f;
            switch (kind)
            {
                case 0x0:
                    return info switch { 0x8 => false, 0x9 => true, _ => null };
                case 0x1:
                    {
                        var size = 1 << info;
                        var v = ReadUInt(d, at + 1, size);
                        return size == 8 ? (long)v : (long)v;
                    }
                case 0x2:
                    return info == 2
                        ? BinaryPrimitives.ReadSingleBigEndian(d.AsSpan(at + 1))
                        : BinaryPrimitives.ReadDoubleBigEndian(d.AsSpan(at + 1));
                case 0x3:
                    return BinaryPrimitives.ReadDoubleBigEndian(d.AsSpan(at + 1)); // a date, as seconds since 2001
                case 0x4:
                    {
                        var (n, start) = Length(at, info);
                        return d.AsSpan(start, n).ToArray();
                    }
                case 0x5:
                    {
                        var (n, start) = Length(at, info);
                        return Encoding.ASCII.GetString(d, start, n);
                    }
                case 0x6:
                    {
                        var (n, start) = Length(at, info);
                        return Encoding.BigEndianUnicode.GetString(d, start, n * 2);
                    }
                case 0x8:
                    return new Uid((int)ReadUInt(d, at + 1, info + 1));
                case 0xA:
                    {
                        var (n, start) = Length(at, info);
                        var list = new List<object?>(n);
                        for (var i = 0; i < n; i++) list.Add(Read((int)ReadUInt(d, start + i * refSize, refSize), depth + 1));
                        return list;
                    }
                case 0xD:
                    {
                        var (n, start) = Length(at, info);
                        var dict = new Dictionary<string, object?>(n);
                        for (var i = 0; i < n; i++)
                        {
                            var key = Read((int)ReadUInt(d, start + i * refSize, refSize), depth + 1) as string ?? $"#{i}";
                            dict[key] = Read((int)ReadUInt(d, start + (n + i) * refSize, refSize), depth + 1);
                        }
                        return dict;
                    }
                default:
                    return null;
            }
        }

        private (int Count, int Start) Length(int at, int info)
        {
            if (info != 0xf) return (info, at + 1);
            var intMarker = d[at + 1];
            var size = 1 << (intMarker & 0x0f);
            return ((int)ReadUInt(d, at + 2, size), at + 2 + size);
        }
    }

    /// <summary>
    /// The root object of an NSKeyedArchiver archive with its fields resolved
    /// one level: strings, numbers and lists of strings come back as values.
    /// </summary>
    public static Dictionary<string, object?> KeyedRoot(byte[] data)
    {
        var result = new Dictionary<string, object?>();
        if (Parse(data) is not Dictionary<string, object?> archive
            || archive.GetValueOrDefault("$objects") is not List<object?> objects
            || archive.GetValueOrDefault("$top") is not Dictionary<string, object?> top
            || top.GetValueOrDefault("root") is not Uid root
            || root.Value >= objects.Count
            || objects[root.Value] is not Dictionary<string, object?> fields)
            return result;

        object? Resolve(object? v, int depth)
        {
            if (depth > 3) return null;
            if (v is Uid u)
            {
                if (u.Value >= objects.Count) return null;
                var o = objects[u.Value];
                if (o is "$null") return null;
                if (o is Dictionary<string, object?> d)
                {
                    if (d.GetValueOrDefault("NS.string") is { } s) return Resolve(s, depth + 1);
                    if (d.GetValueOrDefault("NS.objects") is List<object?> items) return items.Select(i => Resolve(i, depth + 1)).ToList();
                    if (d.GetValueOrDefault("NS.data") is byte[] b) return b;
                    return null;
                }
                return o;
            }
            return v;
        }

        foreach (var (key, value) in fields)
            if (key != "$class") result[key] = Resolve(value, 0);
        return result;
    }
}
