using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Vakage.Core.Sources.WhatsApp;

/// <summary>One chat in an iPhone's WhatsApp database.</summary>
/// <param name="Jid">WhatsApp's id for the contact or group (<c>5561…@s.whatsapp.net</c>, <c>…@g.us</c>).</param>
/// <param name="Kind">person, group or channel.</param>
public sealed class IosPerson(string jid)
{
    public string Jid { get; } = jid;
    public string? Name { get; set; }
    public string? PushName { get; set; }
    public string? Phone { get; set; }
    public string? PhoneLabel { get; set; }
    public string? About { get; set; }
    public string? Business { get; set; }
    public string? Lid { get; set; }
    public string? Username { get; set; }
    public string? PictureSource { get; set; }
}

public sealed record IosCall(string Id, DateTime Utc, double Seconds, long? Outcome, string? Group, bool Incoming, bool Missed, bool Video,
    string? Participants, string? Creator);

public sealed record IosMember(string Jid, string? Name, bool Admin, bool Active);

public sealed record IosGroup(DateTime? CreatedUtc, string? Creator, IReadOnlyList<IosMember> Members);

public sealed record IosChat(long Pk, string Jid, string Name, string Kind, int MessageCount, DateTime? Last, bool Archived = false)
{
    public bool IsGroup => Kind == "group";

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

    /// <summary>
    /// Every chat with at least one message: people, groups (communities
    /// included) and the channels followed. Status updates are left out.
    /// </summary>
    public IReadOnlyList<IosChat> Chats()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.Z_PK, s.ZCONTACTJID, ifnull(s.ZPARTNERNAME, s.ZCONTACTJID), s.ZSESSIONTYPE,
                   (SELECT count(*) FROM ZWAMESSAGE m WHERE m.ZCHATSESSION = s.Z_PK), s.ZLASTMESSAGEDATE, s.ZARCHIVED
            FROM ZWACHATSESSION s
            WHERE s.ZSESSIONTYPE IN (0, 1, 4, 5) AND s.ZCONTACTJID IS NOT NULL
            """;
        using var r = cmd.ExecuteReader();
        var list = new List<IosChat>();
        while (r.Read())
        {
            var count = r.GetInt32(4);
            if (count == 0) continue;
            var kind = r.GetInt32(3) switch { 1 or 4 => "group", 5 => "channel", _ => "person" };
            list.Add(new IosChat(r.GetInt64(0), r.GetString(1), r.GetString(2), kind, count,
                r.IsDBNull(5) ? null : ToLocal(r.GetDouble(5)), !r.IsDBNull(6) && r.GetInt64(6) == 1));
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
                   mi.ZMEDIALOCALPATH, mi.ZTITLE, mi.ZLATITUDE, mi.ZLONGITUDE,
                   m.ZSTANZAID, m.ZFLAGS,
                   m.ZSTARRED, mi.ZMETADATA, mi.ZVCARDSTRING, mi.ZXMPPTHUMBPATH, info.ZRECEIPTINFO,
                   (SELECT d.ZTITLE FROM ZWAMESSAGEDATAITEM d WHERE d.ZMESSAGE = m.Z_PK ORDER BY d.ZINDEX LIMIT 1),
                   (SELECT d.ZSUMMARY FROM ZWAMESSAGEDATAITEM d WHERE d.ZMESSAGE = m.Z_PK ORDER BY d.ZINDEX LIMIT 1),
                   (SELECT d.ZCONTENT1 FROM ZWAMESSAGEDATAITEM d WHERE d.ZMESSAGE = m.Z_PK ORDER BY d.ZINDEX LIMIT 1),
                   mi.ZMOVIEDURATION, mi.ZFILESIZE
            FROM ZWAMESSAGE m
            LEFT JOIN ZWAGROUPMEMBER gm ON gm.Z_PK = m.ZGROUPMEMBER
            LEFT JOIN ZWAMEDIAITEM mi ON mi.Z_PK = m.ZMEDIAITEM
            LEFT JOIN ZWAMESSAGEINFO info ON info.Z_PK = m.ZMESSAGEINFO
            WHERE m.ZCHATSESSION = $c AND m.ZMESSAGEDATE IS NOT NULL
            ORDER BY m.ZMESSAGEDATE, m.Z_PK
            """;
        cmd.Parameters.AddWithValue("$c", chat.Pk);
        using var r = cmd.ExecuteReader();
        var list = new List<ParsedMessage>();
        while (r.Read())
        {
            var utc = CoreDataEpoch.AddSeconds(Math.Floor(r.GetDouble(0)));
            var sentAt = DateTime.SpecifyKind(utc.ToLocalTime(), DateTimeKind.Unspecified);
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
            var message = Map(sentAt, sender, type, text, mediaPath, title,
                r.IsDBNull(11) ? 0 : r.GetDouble(11), r.IsDBNull(12) ? 0 : r.GetDouble(12));

            // What makes a later backup join on seamlessly: WhatsApp's own id
            // for the message, the exact UTC time, and who sent it by id.
            var stanza = Str(r, 13);
            var flags = r.IsDBNull(14) ? 0L : r.GetInt64(14);
            var senderKey = fromMe ? "me" : chat.IsGroup ? Str(r, 8) ?? Str(r, 4) : chat.Jid;
            list.Add(message with
            {
                SourceId = stanza is null ? null : $"{stanza}|{(fromMe ? 1 : 0)}",
                SentUtc = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified),
                SenderKey = message.Kind == MessageKind.System ? null : senderKey,
                Forwarded = (flags & ForwardedFlag) != 0,
                MediaType = message.AttachmentName is null && message.Kind != MessageKind.MediaOmitted ? null : MediaTypeOf(type, mediaPath),
                Extras = ReadExtras(r, type, title, mediaPath),
            });
        }
        return list;
    }

    /// <summary>
    /// Everything else WhatsApp keeps beside a message: what it replies to,
    /// whether it is starred, how often it was forwarded, a link's preview, a
    /// contact card, the thumbnail of a file never downloaded, and the raw
    /// blobs themselves (metadata and receipts), kept whole so nothing is lost
    /// that a later version learns to read (reactions, read receipts, polls).
    /// </summary>
    private static MessageExtras ReadExtras(SqliteDataReader r, int type, string? title, string? mediaPath)
    {
        var meta = r.IsDBNull(16) ? null : (byte[])r[16];
        string? replyTo = null, replySender = null, linkUrl = null, linkSnippet = null, original = null, mentions = null;
        int? forwardScore = null;
        if (meta is { Length: > 8 })
        {
            try
            {
                if (meta[0] == (byte)'b' && meta[1] == (byte)'p')
                {
                    var root = BinaryPlist.KeyedRoot(meta);
                    replyTo = root.GetValueOrDefault("stanzaID") as string;
                    replySender = root.GetValueOrDefault("senderJID") as string;
                    linkUrl = root.GetValueOrDefault("canonicalURL") as string ?? root.GetValueOrDefault("matchedText") as string;
                    linkSnippet = root.GetValueOrDefault("snippet") as string;
                    original = root.GetValueOrDefault("originalSenderJIDForForwardedMessage") as string;
                    forwardScore = root.GetValueOrDefault("forwardingScore") is long score and > 0 ? (int)score : null;
                    if (root.GetValueOrDefault("mentions") is List<object?> list)
                        mentions = string.Join(",", list.OfType<string>());
                }
                else
                {
                    (replyTo, replySender) = Protobuf.Quote(meta);
                }
            }
            catch (Exception e) when (e is FormatException or IndexOutOfRangeException or ArgumentException)
            {
                // Unreadable: the raw blob is still kept.
            }
        }
        var vcard = r.IsDBNull(17) ? null : r.GetString(17);
        var linkTitle = Str(r, 20) ?? (type == 7 ? title : null);
        return new MessageExtras(
            ReplyTo: replyTo,
            ReplySender: replySender,
            Starred: !r.IsDBNull(15) && r.GetInt64(15) == 1,
            ForwardScore: forwardScore,
            LinkUrl: linkUrl ?? Str(r, 22),
            LinkTitle: linkTitle,
            LinkSnippet: linkSnippet ?? Str(r, 21),
            VCard: vcard is not null && vcard.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase) ? vcard : null,
            Mime: vcard is not null && !vcard.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase) && vcard.Length < 100 ? vcard : null,
            ThumbSource: mediaPath is null ? Str(r, 18) : null,
            OriginalSender: original,
            Mentions: string.IsNullOrEmpty(mentions) ? null : mentions,
            RawMetadata: meta,
            RawReceipts: r.IsDBNull(19) ? null : (byte[])r[19],
            Duration: r.IsDBNull(23) || r.GetDouble(23) <= 0 ? null : r.GetDouble(23),
            MediaSize: r.IsDBNull(24) || r.GetInt64(24) <= 0 ? null : r.GetInt64(24));
    }

    /// <summary>Set on nearly every copy of an image passed from chat to chat: the forwarded mark.</summary>
    private const long ForwardedFlag = 0x100;

    private static string MediaTypeOf(int type, string? mediaPath) => type switch
    {
        1 or 28 or 38 => "image",
        2 => "video",
        3 => "audio",
        11 => "gif",
        15 => "sticker",
        8 => "document",
        _ => mediaPath is null ? "document" : MediaTypes.FromName(mediaPath),
    };

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

    /// <summary>
    /// Everyone the phone knows: its address book (name, number, label, the
    /// "about" text, business name), the names people gave themselves, and
    /// their profile pictures, by WhatsApp id.
    /// </summary>
    public IReadOnlyList<IosPerson> People()
    {
        var people = new Dictionary<string, IosPerson>(StringComparer.Ordinal);
        IosPerson Get(string jid) => people.TryGetValue(jid, out var p) ? p : people[jid] = new IosPerson(jid);

        var contacts = Path.Combine(Root, "ContactsV2.sqlite");
        if (File.Exists(contacts))
        {
            using var conn = OpenReadOnly(contacts);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT ZWHATSAPPID, ZFULLNAME, ZPHONENUMBER, ZPHONENUMBERLABEL, ZABOUTTEXT, ZBUSINESSNAME, ZLID, ZGIVENNAME, ZLASTNAME, ZUSERNAME
                FROM ZWAADDRESSBOOKCONTACT WHERE ZWHATSAPPID IS NOT NULL
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var p = Get(r.GetString(0));
                p.Name ??= Str(r, 1) ?? string.Join(' ', new[] { Str(r, 7), Str(r, 8) }.Where(x => x is not null));
                p.Phone ??= Str(r, 2);
                p.PhoneLabel ??= Str(r, 3)?.Replace("_$!<", "").Replace(">!$_", "");
                p.About ??= Str(r, 4)?.Trim();
                p.Business ??= Str(r, 5);
                p.Lid ??= Str(r, 6);
                p.Username ??= Str(r, 9);
            }
        }

        using (var conn = Connect())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ZJID, ZPUSHNAME FROM ZWAPROFILEPUSHNAME WHERE ZJID IS NOT NULL AND ZPUSHNAME IS NOT NULL";
            using var r = cmd.ExecuteReader();
            while (r.Read()) Get(r.GetString(0)).PushName = r.GetString(1);
        }

        // Profile pictures: Media/Profile/<user>-<picture id>.thumb (or .jpg); the newest per person.
        var profile = Path.Combine(Root, "Media", "Profile");
        if (Directory.Exists(profile))
        {
            var byUser = people.Values.SelectMany(p => new[] { (User: p.Jid.Split('@')[0], P: p), (User: p.Lid?.Split('@')[0] ?? "", P: p) })
                .Where(x => x.User.Length > 0).GroupBy(x => x.User).ToDictionary(g => g.Key, g => g.First().P);
            foreach (var file in Directory.GetFiles(profile).OrderBy(f => File.GetLastWriteTimeUtc(f)))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var dash = name.LastIndexOf('-');
                var user = dash > 0 ? name[..dash] : name;
                if (byUser.TryGetValue(user, out var p)) p.PictureSource = file;
                else if (user.Length > 0) Get(user.Contains('-') ? user + "@g.us" : user + "@s.whatsapp.net").PictureSource = file;
            }
        }
        return people.Values.ToList();
    }

    /// <summary>The phone's call history: when, how long, voice or video, in or out, answered or missed, with whom.</summary>
    public IReadOnlyList<IosCall> Calls()
    {
        var path = Path.Combine(Root, "CallHistory.sqlite");
        if (!File.Exists(path)) return [];
        using var conn = OpenReadOnly(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT e.ZCALLIDSTRING, e.ZDATE, e.ZDURATION, e.ZOUTCOME, e.ZGROUPJIDSTRING, a.ZINCOMING, a.ZMISSED, a.ZVIDEO,
                   (SELECT group_concat(p.ZJIDSTRING) FROM ZWACDCALLEVENTPARTICIPANT p WHERE p.Z1PARTICIPANTS = e.Z_PK),
                   e.ZGROUPCALLCREATORUSERJIDSTRING
            FROM ZWACDCALLEVENT e LEFT JOIN ZWAAGGREGATECALLEVENT a ON a.Z_PK = e.Z1CALLEVENTS
            WHERE e.ZDATE IS NOT NULL
            """;
        using var r = cmd.ExecuteReader();
        var list = new List<IosCall>();
        while (r.Read())
        {
            list.Add(new IosCall(
                Id: Str(r, 0) ?? $"call-{r.GetDouble(1).ToString(CultureInfo.InvariantCulture)}",
                Utc: DateTime.SpecifyKind(CoreDataEpoch.AddSeconds(Math.Floor(r.GetDouble(1))), DateTimeKind.Unspecified),
                Seconds: r.IsDBNull(2) ? 0 : r.GetDouble(2),
                Outcome: r.IsDBNull(3) ? null : r.GetInt64(3),
                Group: Str(r, 4),
                Incoming: !r.IsDBNull(5) && r.GetInt64(5) == 1,
                Missed: !r.IsDBNull(6) && r.GetInt64(6) == 1,
                Video: !r.IsDBNull(7) && r.GetInt64(7) == 1,
                Participants: Str(r, 8),
                Creator: Str(r, 9)));
        }
        return list;
    }

    /// <summary>A group's creation, creator, and members (with admins and who has left).</summary>
    public IosGroup? Group(IosChat chat)
    {
        if (!chat.IsGroup) return null;
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ZCREATIONDATE, ZCREATORJID, ZOWNERJID FROM ZWAGROUPINFO WHERE ZCHATSESSION = $c LIMIT 1";
        cmd.Parameters.AddWithValue("$c", chat.Pk);
        DateTime? created = null;
        string? creator = null;
        using (var r = cmd.ExecuteReader())
        {
            if (r.Read())
            {
                created = r.IsDBNull(0) ? null : DateTime.SpecifyKind(CoreDataEpoch.AddSeconds(Math.Floor(r.GetDouble(0))), DateTimeKind.Unspecified);
                creator = Str(r, 1) ?? Str(r, 2);
            }
        }
        using var members = conn.CreateCommand();
        members.CommandText = "SELECT ZMEMBERJID, ZCONTACTNAME, ZFIRSTNAME, ZISADMIN, ZISACTIVE FROM ZWAGROUPMEMBER WHERE ZCHATSESSION = $c AND ZMEMBERJID IS NOT NULL";
        members.Parameters.AddWithValue("$c", chat.Pk);
        var list = new List<IosMember>();
        using (var r = members.ExecuteReader())
            while (r.Read())
                list.Add(new IosMember(r.GetString(0), Str(r, 1) ?? Str(r, 2), !r.IsDBNull(3) && r.GetInt64(3) == 1, r.IsDBNull(4) || r.GetInt64(4) == 1));
        return new IosGroup(created, creator, list);
    }

    /// <summary>Who the phone's owner has blocked.</summary>
    public IReadOnlyList<string> Blocked()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ZJID FROM ZWABLACKLISTITEM WHERE ZJID IS NOT NULL";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>Changes to groups' members WhatsApp recorded apart from the messages.</summary>
    public IReadOnlyList<(string Group, long Type, DateTime Utc, string? Members)> GroupChanges()
    {
        using var conn = Connect();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ZGROUPJID, ZCHANGETYPE, ZCHANGEDATE, ZMEMBERJIDS FROM ZWAGROUPMEMBERSCHANGE WHERE ZGROUPJID IS NOT NULL AND ZCHANGEDATE IS NOT NULL";
        using var r = cmd.ExecuteReader();
        var list = new List<(string, long, DateTime, string?)>();
        while (r.Read())
            list.Add((r.GetString(0), r.IsDBNull(1) ? 0 : r.GetInt64(1),
                DateTime.SpecifyKind(CoreDataEpoch.AddSeconds(Math.Floor(r.GetDouble(2))), DateTimeKind.Unspecified), Str(r, 3)));
        return list;
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conn.Open();
        return conn;
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
