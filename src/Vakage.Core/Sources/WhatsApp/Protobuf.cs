using System.Text;

namespace Vakage.Core.Sources.WhatsApp;

/// <summary>
/// Just enough of the protocol-buffer wire format to read the fields of
/// WhatsApp's message metadata that are certain without its schema: field 5
/// is the id of the message replied to, field 6 its sender. Everything else
/// stays in the raw blob, kept beside the message.
/// </summary>
public static class Protobuf
{
    public static (string? ReplyTo, string? ReplySender) Quote(byte[] data)
    {
        string? replyTo = null, sender = null;
        var i = 0;
        while (i < data.Length)
        {
            var key = Varint(data, ref i);
            int field = (int)(key >> 3), wire = (int)(key & 7);
            switch (wire)
            {
                case 0:
                    Varint(data, ref i);
                    break;
                case 1:
                    i += 8;
                    break;
                case 5:
                    i += 4;
                    break;
                case 2:
                    var length = (int)Varint(data, ref i);
                    if (length < 0 || i + length > data.Length) return (replyTo, sender);
                    if (field is 5 or 6 && length is > 0 and < 128)
                    {
                        var text = Encoding.UTF8.GetString(data, i, length);
                        if (text.All(c => c is > ' ' and < '\u007f'))
                        {
                            if (field == 5) replyTo = text;
                            else if (text.Contains('@')) sender = text;
                        }
                    }
                    i += length;
                    break;
                default:
                    return (replyTo, sender); // groups and unknown wire types: stop, keep what was read
            }
        }
        return (replyTo, sender);
    }

    private static ulong Varint(byte[] data, ref int i)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64 && i < data.Length; shift += 7)
        {
            var b = data[i++];
            value |= (ulong)(b & 0x7f) << shift;
            if (b < 0x80) return value;
        }
        return value;
    }
}
