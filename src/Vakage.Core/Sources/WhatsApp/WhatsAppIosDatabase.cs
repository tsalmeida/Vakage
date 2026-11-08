using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Vakage.Core.Sources.WhatsApp;

/// <summary>One chat in an iPhone's WhatsApp database.</summary>
/// <param name="Jid">WhatsApp's id for the contact or group (<c>5561…@s.whatsapp.net</c>, <c>…@g.us</c>).</param>
public sealed record IosChat(long Pk, string Jid, string Name, bool IsGroup, int MessageCount, DateTime? Last)
{
    /// <summary>The phone number in the id, for telling apart contacts saved under one name.</summary>
    public string Number => Jid.Split('@')[0];
}

/// <summary>
/// Reads WhatsApp's own database from an iPhone: the shared container
/// (<c>group.net.whatsapp.WhatsApp.shared</c>) as it comes out of a decrypted
/// iPhone backup, holding <c>ChatStorage.sqlite</c> and <c>Message\Media</c>.
/// Unlike an exported chat it has every chat, exact times, the real names of
/// documents, and every downloaded file. Nothing here writes to it.
/// </summary>
public sealed class WhatsAppIosDatabase
{
    private static readonly DateTime CoreDataEpoch = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public string Root { get; }
    private string DatabasePath => Path.Combine(Root, "ChatStorage.sqlite");

    private WhatsAppIosDatabase(string root) => Root = root;

    /// <summary>Opens the shared container, or a folder above it, or ChatStorage.sqlite itself.</summary>
    public static WhatsAppIosDatabase Open(string path)
    {
        var full = Path.GetFullPath(path);
        if (File.Exists(full) && Path.GetFileName(full).Equals("ChatStorage.sqlite", StringComparison.OrdinalIgnoreCase))
            return new WhatsAppIosDatabase(Path.GetDirectoryName(full)!);
        if (File.Exists(Path.Combine(full, "ChatStorage.sqlite")))
            return new WhatsAppIosDatabase(full);
        var nested = Path.Combine(full, "group.net.whatsapp.WhatsApp.shared");
        if (File.Exists(Path.Combine(nested, "ChatStorage.sqlite")))
            return new WhatsAppIosDatabase(nested);
        throw new FileNotFoundException("No ChatStorage.sqlite here: point at WhatsApp's shared container from an iPhone backup.", path);
    }

    /// <summary>True when the path holds an iPhone WhatsApp database rather than an exported chat.</summary>
    public static bool IsDatabase(string path)
    {
        try { Open(path); return true; }
        catch (FileNotFoundException) { return false; }
    }

    private SqliteConnection Connect()
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        conn.Open();
        return conn;
    }

    /// <summary>Every person and group chat with at least one message; status updates are left out.</summary>
    public IReadOnlyList<IosChat> Chats()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.Z_PK, s.ZCONTACTJID, ifnull(s.ZPARTNERNAME, s.ZCONTACTJID), s.ZSESSIONTYPE,
                   (SELECT count(*) FROM ZWAMESSAGE m WHERE m.ZCHATSESSION = s.Z_PK), s.ZLASTMESSAGEDATE
            FROM ZWACHATSESSION s
            WHERE s.ZSESSIONTYPE IN (0, 1) AND s.ZCONTACTJID IS NOT NULL
            """;
        using var r = cmd.ExecuteReader();
        var list = new List<IosChat>();
        while (r.Read())
        {
            var count = r.GetInt32(4);
            if (count == 0) continue;
            list.Add(new IosChat(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3) == 1, count,
                r.IsDBNull(5) ? null : ToLocal(r.GetDouble(5))));
        }
        return list.OrderByDescending(c => c.Last ?? DateTime.MinValue).ToList();
    }

    /// <summary>
    /// A chat's messages, oldest first, in the form every importer hands the
    /// library. The phone's owner is written as <paramref name="selfName"/>.
    /// Times are the phone's UTC times shown in this computer's time zone.
    /// </summary>
    public IReadOnlyList<ParsedMessage> ReadMessages(IosChat chat, string selfName)
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT m.ZMESSAGEDATE, m.ZISFROMME, m.ZMESSAGETYPE, m.ZTEXT, m.ZFROMJID, m.ZPUSHNAME,
                   gm.ZCONTACTNAME, gm.ZFIRSTNAME, gm.ZMEMBERJID,
                   mi.ZMEDIALOCALPATH, mi.ZTITLE, mi.ZLATITUDE, mi.ZLONGITUDE
            FROM ZWAMESSAGE m
            LEFT JOIN ZWAGROUPMEMBER gm ON gm.Z_PK = m.ZGROUPMEMBER
            LEFT JOIN ZWAMEDIAITEM mi ON mi.Z_PK = m.ZMEDIAITEM
            WHERE m.ZCHATSESSION = $c AND m.ZMESSAGEDATE IS NOT NULL
            ORDER BY m.ZMESSAGEDATE, m.Z_PK
            """;
        cmd.Parameters.AddWithValue("$c", chat.Pk);
        using var r = cmd.ExecuteReader();
        var list = new List<ParsedMessage>();
        while (r.Read())
        {
            var sentAt = ToLocal(r.GetDouble(0));
            var fromMe = !r.IsDBNull(1) && r.GetInt64(1) == 1;
            var type = r.IsDBNull(2) ? 0 : r.GetInt32(2);
            var text = r.IsDBNull(3) ? "" : r.GetString(3).Replace("‎", "");
            var sender = fromMe
                ? selfName
                : chat.IsGroup
                    ? Str(r, 6) ?? Str(r, 7) ?? Str(r, 5) ?? Number(Str(r, 8) ?? Str(r, 4)) ?? "Unknown"
                    : chat.Name;
            var mediaPath = Str(r, 9);
            var title = Str(r, 10);
            list.Add(Map(sentAt, sender, type, text, mediaPath, title,
                r.IsDBNull(11) ? 0 : r.GetDouble(11), r.IsDBNull(12) ? 0 : r.GetDouble(12)));
        }
        return list;
    }

    private static ParsedMessage Map(DateTime sentAt, string sender, int type, string text, string? mediaPath, string? title, double lat, double lon)
    {
        switch (type)
        {
            case 0 or 7: // text; a link carries its URL in the text
                return new ParsedMessage(sentAt, sender, text, null, MessageKind.Text);
            case 6: // group events: created, joined, subject changed
                return new ParsedMessage(sentAt, null, text.Length > 0 ? $"{sender}: {text}" : "A change to the group.", null, MessageKind.System);
            case 14:
                return new ParsedMessage(sentAt, null, $"{sender} deleted a message.", null, MessageKind.System);
            case 59:
                return new ParsedMessage(sentAt, null, $"A call with {sender}.", null, MessageKind.System);
            case 5:
                return new ParsedMessage(sentAt, sender,
                    string.Format(CultureInfo.InvariantCulture, "Location: {0:0.#####}, {1:0.#####}", lat, lon) + (title is null ? "" : $" ({title})"),
                    null, MessageKind.Text);
            case 4:
                return new ParsedMessage(sentAt, sender, $"Contact card: {(text.Length > 0 ? text : title ?? "")}".TrimEnd(' ', ':'), null, MessageKind.Text);
        }

        if (mediaPath is not null)
        {
            // A document keeps its real name in the text (or the title); a
            // photo, video or voice note is named by the file WhatsApp stored,
            // and its caption is the media item's title.
            string name, caption;
            if (type == 8)
            {
                name = text.Length > 0 ? text : title ?? Path.GetFileName(mediaPath);
                caption = text.Length > 0 && title is not null && title != text ? title : "";
                name = name.Replace('/', '-').Replace('\\', '-');
                if (!Path.HasExtension(name)) name += Path.GetExtension(mediaPath);
            }
            else
            {
                name = Path.GetFileName(mediaPath);
                caption = text.Length > 0 ? text : title ?? "";
            }
            return new ParsedMessage(sentAt, sender, caption, name, MessageKind.Text, AttachmentSource: mediaPath);
        }

        return type switch
        {
            1 or 2 or 3 or 8 or 11 or 15 or 28 or 38 => new ParsedMessage(sentAt, sender, text, null, MessageKind.MediaOmitted),
            _ when text.Length > 0 => new ParsedMessage(sentAt, sender, text, null, MessageKind.Text),
            _ => new ParsedMessage(sentAt, null, $"A WhatsApp notice (type {type}).", null, MessageKind.System),
        };
    }

    /// <summary>Opens a stored file by the path WhatsApp recorded (<c>Media/…</c>, under <c>Message</c>).</summary>
    public Stream? OpenFile(string mediaPath)
    {
        var relative = mediaPath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        foreach (var candidate in new[] { Path.Combine(Root, "Message", relative), Path.Combine(Root, relative) })
            if (File.Exists(candidate)) return File.OpenRead(candidate);
        return null;
    }

    private static DateTime ToLocal(double coreDataSeconds)
        => DateTime.SpecifyKind(CoreDataEpoch.AddSeconds(coreDataSeconds).ToLocalTime(), DateTimeKind.Unspecified);

    private static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) || r.GetString(i).Length == 0 ? null : r.GetString(i);

    private static string? Number(string? jid) => jid is null ? null : "+" + jid.Split('@')[0];
}
