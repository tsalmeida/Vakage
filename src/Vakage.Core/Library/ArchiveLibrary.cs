using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Vakage.Core.Sources.WhatsApp;

namespace Vakage.Core.Library;

public sealed record Account(long Id, string Name, string? SelfName);

/// <summary>A chat as one export (or several exports of the same chat) brought it in.</summary>
/// <param name="Kind">person, group or channel; null when the source did not say (an export).</param>
public sealed record Chat(long Id, long AccountId, string Source, string Title, long? ThreadId, int MessageCount, DateTime? First, DateTime? Last, string? Kind = null,
    bool Noisy = false);

/// <summary>
/// What the conversation list shows: a chat on its own, or a joined thread of
/// several chats of the same account read as one stream.
/// </summary>
public sealed record Conversation(long? ChatId, long? ThreadId, string Title, int MessageCount, DateTime? First, DateTime? Last, IReadOnlyList<long> ChatIds)
{
    public bool IsJoined => ThreadId is not null;
}

/// <param name="SentAt">The time to show: the exact UTC time in this computer's zone when the source knew it, else the wall clock the export gave.</param>
/// <param name="Reduced">How an irrelevant file was let go: thumbnail, text or removed; null while the original is kept.</param>
public sealed record StoredMessage(long Id, long ChatId, DateTime SentAt, string? Sender, string Body, MessageKind Kind, string? AttachmentName,
    long? FileId, string? FilePath, long? FileSize, bool Forwarded = false, string? Reduced = null, string? ThumbPath = null, string? FileText = null,
    bool Starred = false, string? QuotedSender = null, string? QuotedBody = null, string? LinkTitle = null, string? LinkUrl = null,
    string? MessageThumb = null, string? VCard = null, int? ForwardScore = null, string? SenderKey = null, bool MediaDeleted = false,
    double? Duration = null)
{
    /// <summary>Written by the account's owner.</summary>
    public bool FromMe => SenderKey == "me";
}

/// <summary>One stored file, with who sent it and where. A file sent many times is one row.</summary>
/// <param name="Mark">important or irrelevant, as the person decided; null when undecided.</param>
/// <param name="Suggestion">important or irrelevant, as Vakage guessed, with <paramref name="Reason"/>.</param>
/// <param name="Reduced">thumbnail, text or removed once the original is gone; null while it is kept.</param>
public sealed record StoredFile(long Id, string Sha256, long Size, string Name, string Path, string Senders, string Chats, DateTime FirstSent, int References,
    string MediaType = "document", string? Mark = null, string? Suggestion = null, string? Reason = null, string? Reduced = null,
    string? ThumbPath = null, string? Text = null)
{
    /// <summary>The list the file is on: the person's mark, else Vakage's suggestion.</summary>
    public string? Standing => Mark ?? Suggestion;
}

public sealed record ImportResult(long ChatId, string Title, bool NewChat, int MessagesAdded, int MessagesAlreadyThere, int FilesStored, int FilesAlreadyStored, int FilesMissing);

/// <summary>
/// The archive on disk: <c>vakage.sqlite</c> indexes accounts, chats and
/// messages, and <c>files\ab\&lt;sha256&gt;.ext</c> holds each attached file
/// once, by its content. Two chats, or two accounts, that carry the same photo
/// point at the same file, so a file is never stored twice however many
/// exports bring it in. Accounts never mix: every chat belongs to exactly one,
/// and a joined thread only joins chats of one account.
/// </summary>
public sealed partial class ArchiveLibrary
{
    public const string DatabaseName = "vakage.sqlite";

    public string Root { get; }
    public string DatabasePath => Path.Combine(Root, DatabaseName);
    public string FilesRoot => Path.Combine(Root, "files");

    private ArchiveLibrary(string root) => Root = root;

    public static ArchiveLibrary Open(string root)
    {
        Directory.CreateDirectory(root);
        var library = new ArchiveLibrary(Path.GetFullPath(root));
        library.Migrate();
        return library;
    }

    private SqliteConnection Connect()
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 120,
        }.ToString());
        conn.Open();
        // Another Vakage (a reading run, the window) may be writing: wait for it rather than fail.
        using var wait = conn.CreateCommand();
        wait.CommandText = "PRAGMA busy_timeout = 60000";
        wait.ExecuteNonQuery();
        return conn;
    }

    private void Migrate()
    {
        using var conn = Connect();
        Exec(conn, """
            PRAGMA journal_mode = DELETE;
            CREATE TABLE IF NOT EXISTS accounts (
              id INTEGER PRIMARY KEY,
              name TEXT NOT NULL UNIQUE,
              self_name TEXT,
              created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS threads (
              id INTEGER PRIMARY KEY,
              account_id INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
              title TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS chats (
              id INTEGER PRIMARY KEY,
              account_id INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
              source TEXT NOT NULL,
              title TEXT NOT NULL,
              thread_id INTEGER REFERENCES threads(id) ON DELETE SET NULL,
              created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS imports (
              id INTEGER PRIMARY KEY,
              chat_id INTEGER NOT NULL REFERENCES chats(id) ON DELETE CASCADE,
              source_path TEXT NOT NULL,
              imported_at TEXT NOT NULL,
              messages_added INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS files (
              id INTEGER PRIMARY KEY,
              sha256 TEXT NOT NULL UNIQUE,
              size INTEGER NOT NULL,
              ext TEXT NOT NULL,
              name TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS messages (
              id INTEGER PRIMARY KEY,
              chat_id INTEGER NOT NULL REFERENCES chats(id) ON DELETE CASCADE,
              seq INTEGER NOT NULL,
              sent_at TEXT NOT NULL,
              sender TEXT,
              body TEXT NOT NULL,
              kind TEXT NOT NULL,
              attachment_name TEXT,
              file_id INTEGER REFERENCES files(id)
            );
            CREATE INDEX IF NOT EXISTS messages_chat ON messages(chat_id, sent_at, seq);
            CREATE INDEX IF NOT EXISTS messages_file ON messages(file_id);
            CREATE INDEX IF NOT EXISTS chats_account ON chats(account_id);
            """);
        // A chat read from a phone's own database is known by its WhatsApp id
        // (the contact's JID), so two contacts saved under one name stay apart.
        // The library lives in a synced folder, so it is one file: a rollback
        // journal, never WAL's side files. Columns added since 0.1 come in here.
        AddColumns(conn, "chats", ("source_key", "TEXT"), ("kind", "TEXT"));
        AddColumns(conn, "messages", ("source_id", "TEXT"), ("sent_utc", "TEXT"), ("sender_key", "TEXT"),
            ("forwarded", "INTEGER NOT NULL DEFAULT 0"), ("media_type", "TEXT"));
        AddColumns(conn, "files", ("mark", "TEXT"), ("suggestion", "TEXT"), ("suggestion_reason", "TEXT"),
            ("reduced", "TEXT"), ("thumb", "TEXT"), ("text", "TEXT"), ("reduced_at", "TEXT"),
            ("width", "INTEGER"), ("height", "INTEGER"), ("ocr", "TEXT"), ("ocr_words", "INTEGER"), ("doc_kind", "TEXT"));
        AddColumns(conn, "messages", ("reply_to", "TEXT"), ("reply_sender", "TEXT"), ("starred", "INTEGER NOT NULL DEFAULT 0"),
            ("forward_score", "INTEGER"), ("link_url", "TEXT"), ("link_title", "TEXT"), ("link_snippet", "TEXT"), ("vcard", "TEXT"),
            ("mime", "TEXT"), ("thumb", "TEXT"), ("original_sender", "TEXT"), ("mentions", "TEXT"), ("raw_meta", "BLOB"), ("raw_receipts", "BLOB"));
        AddColumns(conn, "chats", ("created_utc", "TEXT"), ("creator_key", "TEXT"), ("noisy", "INTEGER NOT NULL DEFAULT 0"),
            ("archived", "INTEGER NOT NULL DEFAULT 0"));
        AddColumns(conn, "messages", ("duration", "REAL"), ("media_size", "INTEGER"), ("media_deleted", "INTEGER NOT NULL DEFAULT 0"));
        Exec(conn, """
            CREATE TABLE IF NOT EXISTS excluded_messages (
              chat_id INTEGER NOT NULL REFERENCES chats(id) ON DELETE CASCADE,
              source_id TEXT NOT NULL,
              PRIMARY KEY (chat_id, source_id)
            );
            """);
        Exec(conn, """
            CREATE TABLE IF NOT EXISTS people (
              account_id INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
              key TEXT NOT NULL,
              name TEXT, push_name TEXT, phone TEXT, phone_label TEXT, about TEXT, business TEXT, lid TEXT, username TEXT,
              picture TEXT,
              PRIMARY KEY (account_id, key)
            );
            CREATE TABLE IF NOT EXISTS calls (
              id INTEGER PRIMARY KEY,
              account_id INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
              source_id TEXT NOT NULL,
              started_utc TEXT NOT NULL,
              seconds REAL NOT NULL,
              video INTEGER NOT NULL, incoming INTEGER NOT NULL, missed INTEGER NOT NULL, outcome INTEGER,
              group_key TEXT, participants TEXT, creator_key TEXT,
              UNIQUE (account_id, source_id)
            );
            CREATE TABLE IF NOT EXISTS chat_members (
              chat_id INTEGER NOT NULL REFERENCES chats(id) ON DELETE CASCADE,
              member_key TEXT NOT NULL,
              name TEXT, admin INTEGER NOT NULL, active INTEGER NOT NULL,
              PRIMARY KEY (chat_id, member_key)
            );
            CREATE TABLE IF NOT EXISTS blocked (
              account_id INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
              key TEXT NOT NULL,
              PRIMARY KEY (account_id, key)
            );
            CREATE TABLE IF NOT EXISTS group_changes (
              account_id INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
              group_key TEXT NOT NULL, change_type INTEGER NOT NULL, changed_utc TEXT NOT NULL, members TEXT,
              PRIMARY KEY (account_id, group_key, change_type, changed_utc)
            );
            CREATE TABLE IF NOT EXISTS excluded_chats (
              account_id INTEGER NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
              source_key TEXT NOT NULL,
              title TEXT,
              excluded_at TEXT NOT NULL,
              PRIMARY KEY (account_id, source_key)
            );
            """);
        Exec(conn, "CREATE INDEX IF NOT EXISTS messages_source ON messages(chat_id, source_id)");
        Exec(conn, "CREATE INDEX IF NOT EXISTS messages_sender ON messages(sender_key, sender)");
    }

    private static void AddColumns(SqliteConnection conn, string table, params (string Name, string Type)[] columns)
    {
        var have = Query(conn, $"SELECT name FROM pragma_table_info('{table}')", r => r.GetString(0));
        foreach (var (name, type) in columns)
            if (!have.Contains(name))
                Exec(conn, $"ALTER TABLE {table} ADD COLUMN {name} {type}");
    }

    // ----- Accounts -----

    public IReadOnlyList<Account> Accounts()
    {
        using var conn = Connect();
        // The account with the latest message first, as Tekhnexos orders everything.
        return Query(conn, """
            SELECT a.id, a.name, a.self_name FROM accounts a
            ORDER BY (SELECT max(coalesce(m.sent_utc, m.sent_at)) FROM messages m JOIN chats c ON c.id = m.chat_id WHERE c.account_id = a.id) DESC, a.created_at DESC
            """,
            r => new Account(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
    }

    public Account AddAccount(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("An account needs a name.", nameof(name));
        using var conn = Connect();
        var existing = Query(conn, "SELECT id, name, self_name FROM accounts WHERE name = $n COLLATE NOCASE",
            r => new Account(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)), ("$n", name));
        if (existing.Count > 0) return existing[0];
        var id = Scalar(conn, "INSERT INTO accounts (name, created_at) VALUES ($n, $t) RETURNING id", ("$n", name), ("$t", Now()));
        return new Account(id, name, null);
    }

    /// <summary>Which sender is the account's owner, so their messages sit on the right.</summary>
    public void SetSelfName(long accountId, string? selfName)
    {
        using var conn = Connect();
        Exec(conn, "UPDATE accounts SET self_name = $s WHERE id = $a", ("$s", (object?)selfName ?? DBNull.Value), ("$a", accountId));
    }

    /// <summary>
    /// The owner's name as the exports write it: the one set by hand, else the
    /// sender found in every chat of the account (it takes two chats to tell).
    /// </summary>
    public string? SelfNameFor(long accountId)
    {
        using var conn = Connect();
        var set = Query(conn, "SELECT self_name FROM accounts WHERE id = $a", r => r.IsDBNull(0) ? null : r.GetString(0), ("$a", accountId));
        if (set.Count > 0 && set[0] is { } name) return name;
        var chats = Scalar(conn, "SELECT count(*) FROM chats WHERE account_id = $a", ("$a", accountId));
        if (chats < 2) return null;
        var found = Query(conn, """
            SELECT m.sender FROM messages m JOIN chats c ON c.id = m.chat_id
            WHERE c.account_id = $a AND m.sender IS NOT NULL
            GROUP BY m.sender HAVING count(DISTINCT m.chat_id) = $n
            ORDER BY count(*) DESC LIMIT 1
            """, r => r.GetString(0), ("$a", accountId), ("$n", chats));
        return found.Count > 0 ? found[0] : null;
    }

    /// <summary>Everyone who wrote in the account's chats, whoever wrote last first.</summary>
    public IReadOnlyList<string> Senders(long accountId)
    {
        using var conn = Connect();
        return Query(conn, """
            SELECT m.sender FROM messages m JOIN chats c ON c.id = m.chat_id
            WHERE c.account_id = $a AND m.sender IS NOT NULL
            GROUP BY m.sender ORDER BY max(coalesce(m.sent_utc, m.sent_at)) DESC
            """, r => r.GetString(0), ("$a", accountId));
    }

    // ----- Import -----

    /// <summary>
    /// Brings one WhatsApp export into an account. A chat of the same title in
    /// that account takes the new messages only, so a later export of the same
    /// chat adds what is new and repeats nothing. Every attached file is copied
    /// into the store (never moved: the export is left as it was).
    /// </summary>
    public ImportResult ImportWhatsApp(long accountId, string exportPath, string? titleOverride = null)
    {
        using var export = WhatsAppExport.Open(exportPath);
        var title = string.IsNullOrWhiteSpace(titleOverride) ? export.Title : titleOverride.Trim();
        return ImportMessages(accountId, "whatsapp", sourceKey: null, title, export.ReadMessages(), export.OpenFile, export.SourcePath);
    }

    /// <summary>
    /// Brings messages from any source into a chat of an account. The chat is
    /// found by <paramref name="sourceKey"/> when the source has one (a phone's
    /// own database), else by title; a chat already there takes only the
    /// messages it does not hold yet. Each attached file is opened through
    /// <paramref name="openFile"/> (by the message's attachment source) and
    /// copied into the store.
    /// </summary>
    public ImportResult ImportMessages(long accountId, string source, string? sourceKey, string title,
        IReadOnlyList<ParsedMessage> messages, Func<string, Stream?> openFile, string sourcePath, string? chatKind = null,
        IReadOnlySet<string>? liveKeys = null)
    {
        using var conn = Connect();
        using var tx = conn.BeginTransaction();

        var chatIds = sourceKey is not null
            ? Query(conn, "SELECT id FROM chats WHERE account_id = $a AND source = $s AND source_key = $k",
                r => r.GetInt64(0), ("$a", accountId), ("$s", source), ("$k", sourceKey))
            : Query(conn, "SELECT id FROM chats WHERE account_id = $a AND source = $s AND title = $t AND source_key IS NULL",
                r => r.GetInt64(0), ("$a", accountId), ("$s", source), ("$t", title));
        if (chatIds.Count == 0 && sourceKey is not null
            && FindRekeyedChat(conn, accountId, source, messages, liveKeys) is { } rekeyed)
        {
            chatIds = [rekeyed];
            Exec(conn, "UPDATE chats SET source_key = $k WHERE id = $c", ("$k", sourceKey), ("$c", rekeyed));
        }
        var newChat = chatIds.Count == 0;
        var chatId = newChat
            ? Scalar(conn, "INSERT INTO chats (account_id, source, source_key, title, kind, created_at) VALUES ($a, $s, $k, $t, $d, $n) RETURNING id",
                ("$a", accountId), ("$s", source), ("$k", (object?)sourceKey ?? DBNull.Value), ("$t", title), ("$d", (object?)chatKind ?? DBNull.Value), ("$n", Now()))
            : chatIds[0];
        if (!newChat && sourceKey is not null)
            // The contact's or group's current name, as the phone has it now.
            Exec(conn, "UPDATE chats SET title = $t, kind = ifnull($d, kind) WHERE id = $c", ("$t", title), ("$d", (object?)chatKind ?? DBNull.Value), ("$c", chatId));

        // Messages the chat holds already, by the source's own id, and those deleted on purpose.
        var knownIds = newChat
            ? new HashSet<string>(StringComparer.Ordinal)
            : Query(conn, "SELECT source_id FROM messages WHERE chat_id = $c AND source_id IS NOT NULL UNION SELECT source_id FROM excluded_messages WHERE chat_id = $c",
                    r => r.GetString(0), ("$c", chatId))
                .ToHashSet(StringComparer.Ordinal);

        // What the chat already holds, counted: a message repeated in the same
        // minute ("ok", "ok") is two messages, and a later export repeats both.
        var already = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!newChat)
        {
            foreach (var key in Query(conn, "SELECT sent_at, ifnull(sender, ''), body, ifnull(attachment_name, ''), kind FROM messages WHERE chat_id = $c",
                         r => Key(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)), ("$c", chatId)))
                already[key] = already.GetValueOrDefault(key) + 1;
        }
        var seq = Scalar(conn, "SELECT ifnull(max(seq), 0) FROM messages WHERE chat_id = $c", ("$c", chatId));

        int added = 0, skipped = 0, stored = 0, storedBefore = 0, missing = 0;
        foreach (var m in messages)
        {
            var sentAt = Stamp(m.SentAt);
            var kind = KindName(m.Kind);
            if (m.SourceId is not null)
            {
                if (!knownIds.Add(m.SourceId))
                {
                    // Already archived: refresh what the phone says about it now
                    // (starred, its preview, the blobs), never its text or file.
                    if (m.Extras is { } known)
                        WriteExtras(conn, "WHERE chat_id = $c AND source_id = $sid", known, ("$c", chatId), ("$sid", m.SourceId));
                    skipped++;
                    continue;
                }
            }
            else
            {
                var key = Key(sentAt, m.Sender ?? "", m.Body, m.AttachmentName ?? "", kind);
                if (already.TryGetValue(key, out var count) && count > 0)
                {
                    already[key] = count - 1;
                    skipped++;
                    continue;
                }
            }

            long? fileId = null;
            if (m.AttachmentName is { } name)
            {
                using var stream = openFile(m.AttachmentSource ?? name);
                if (stream is null)
                {
                    missing++;
                }
                else
                {
                    var (id, isNew) = StoreFile(conn, stream, name, m.AttachmentSource);
                    fileId = id;
                    if (isNew) stored++; else storedBefore++;
                }
            }

            Exec(conn, """
                INSERT INTO messages (chat_id, seq, sent_at, sender, body, kind, attachment_name, file_id,
                                      source_id, sent_utc, sender_key, forwarded, media_type)
                VALUES ($c, $q, $t, $s, $b, $k, $an, $f, $sid, $utc, $sk, $fw, $mt)
                """,
                ("$c", chatId), ("$q", ++seq), ("$t", sentAt), ("$s", (object?)m.Sender ?? DBNull.Value), ("$b", m.Body),
                ("$k", kind), ("$an", (object?)m.AttachmentName ?? DBNull.Value), ("$f", (object?)fileId ?? DBNull.Value),
                ("$sid", (object?)m.SourceId ?? DBNull.Value), ("$utc", m.SentUtc is { } u ? Stamp(u) : DBNull.Value),
                ("$sk", (object?)m.SenderKey ?? DBNull.Value), ("$fw", m.Forwarded ? 1 : 0), ("$mt", (object?)m.MediaType ?? DBNull.Value));
            if (m.Extras is { } extras)
                WriteExtras(conn, "WHERE id = last_insert_rowid()", extras);
            added++;
        }

        _thumbSource = null;
        Exec(conn, "INSERT INTO imports (chat_id, source_path, imported_at, messages_added) VALUES ($c, $p, $t, $n)",
            ("$c", chatId), ("$p", sourcePath), ("$t", Now()), ("$n", added));
        tx.Commit();
        return new ImportResult(chatId, title, newChat, added, skipped, stored, storedBefore, missing);
    }

    /// <summary>
    /// Brings chats from an iPhone's WhatsApp database (the shared container of
    /// a decrypted backup) into an account. <paramref name="chatFilter"/> picks
    /// chats by name or number (any chat whose name or number contains one of
    /// the terms); with none, every chat comes in. Each chat is known by its
    /// WhatsApp id, so contacts saved under one name stay apart, their titles
    /// carrying the number, and a later backup adds only what is new.
    /// </summary>
    /// <summary>Opens a thumbnail named in a message's extras, from the source being imported.</summary>
    private Func<string, Stream?>? _thumbSource;

    private void WriteExtras(SqliteConnection conn, string where, MessageExtras x, params (string Name, object Value)[] keys)
    {
        string? thumb = null;
        if (x.ThumbSource is { } source && _thumbSource?.Invoke(source) is { } stream)
        {
            using (stream) thumb = StoreSmall(stream, "thumbs", ".jpg");
        }
        var parameters = new List<(string, object)>(keys)
        {
            ("$rt", (object?)x.ReplyTo ?? DBNull.Value), ("$rs", (object?)x.ReplySender ?? DBNull.Value), ("$st", x.Starred ? 1 : 0),
            ("$fs", (object?)x.ForwardScore ?? DBNull.Value), ("$lu", (object?)x.LinkUrl ?? DBNull.Value), ("$lt", (object?)x.LinkTitle ?? DBNull.Value),
            ("$ls", (object?)x.LinkSnippet ?? DBNull.Value), ("$vc", (object?)x.VCard ?? DBNull.Value), ("$mi", (object?)x.Mime ?? DBNull.Value),
            ("$th", (object?)thumb ?? DBNull.Value), ("$os", (object?)x.OriginalSender ?? DBNull.Value), ("$me", (object?)x.Mentions ?? DBNull.Value),
            ("$rm", (object?)x.RawMetadata ?? DBNull.Value), ("$rr", (object?)x.RawReceipts ?? DBNull.Value),
            ("$du", (object?)x.Duration ?? DBNull.Value), ("$ms", (object?)x.MediaSize ?? DBNull.Value),
        };
        Exec(conn, $"""
            UPDATE messages SET reply_to = $rt, reply_sender = $rs, starred = $st, forward_score = $fs, link_url = $lu, link_title = $lt,
                   link_snippet = $ls, vcard = $vc, mime = $mi, thumb = coalesce($th, thumb), original_sender = $os, mentions = $me,
                   raw_meta = $rm, raw_receipts = $rr, duration = $du, media_size = $ms
            {where}
            """, parameters.ToArray());
    }

    /// <summary>A small file (a thumbnail, a profile picture) kept by its content under a folder of the library.</summary>
    private string StoreSmall(Stream source, string folder, string ext)
    {
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        var sha = Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
        var relative = Path.Combine(folder, sha[..2], sha + ext);
        var full = Path.Combine(Root, relative);
        if (!File.Exists(full))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, buffer.ToArray());
        }
        return relative;
    }

    public IReadOnlyList<ImportResult> ImportWhatsAppIos(long accountId, string path, IReadOnlyList<string>? chatFilter = null,
        Action<string>? progress = null, bool includeChannels = false)
    {
        var db = WhatsAppIosDatabase.Open(path);
        var account = Accounts().First(a => a.Id == accountId);
        var self = SelfNameFor(accountId) ?? account.Name;
        if (account.SelfName is null) SetSelfName(accountId, self);

        var chats = db.Chats();
        var liveKeys = chats.Select(c => "ios:" + c.Jid).ToHashSet(StringComparer.Ordinal);
        // Chats deleted on purpose never come back, and channels (broadcast
        // feeds, not conversations) are left out unless asked for.
        var excluded = ExcludedKeys(accountId);
        var skippedChats = chats.Where(c => excluded.Contains("ios:" + c.Jid) || (!includeChannels && c.Kind == "channel")).ToList();
        if (skippedChats.Count > 0)
            progress?.Invoke($"Leaving out {skippedChats.Count} chat(s): deleted before, or channels");
        chats = chats.Except(skippedChats).ToList();
        var sharedNames = chats.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var picked = chatFilter is { Count: > 0 }
            ? chats.Where(c => chatFilter.Any(f => c.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || c.Number.Contains(f.TrimStart('+'), StringComparison.Ordinal))).ToList()
            : chats.ToList();

        var results = new List<ImportResult>();
        foreach (var chat in picked)
        {
            var title = sharedNames.Contains(chat.Name) && chat.Kind == "person" ? $"{chat.Name} (+{chat.Number})" : chat.Name;
            progress?.Invoke($"{title}: {chat.MessageCount:N0} messages");
            _thumbSource = db.OpenFile;
            results.Add(ImportMessages(accountId, "whatsapp", "ios:" + chat.Jid, title,
                db.ReadMessages(chat, self), db.OpenFile, db.Root, chat.Kind, liveKeys));
            if (db.Group(chat) is { } group)
                SaveGroup(results[^1].ChatId, group);
            using (var conn = Connect())
                Exec(conn, "UPDATE chats SET archived = $a WHERE id = $c", ("$a", chat.Archived ? 1 : 0), ("$c", results[^1].ChatId));
        }
        using (var conn = Connect())
        {
            using var tx = conn.BeginTransaction();
            foreach (var key in db.Blocked())
                Exec(conn, "INSERT OR IGNORE INTO blocked (account_id, key) VALUES ($a, $k)", ("$a", accountId), ("$k", key));
            foreach (var (group, type, utc, members) in db.GroupChanges())
                Exec(conn, "INSERT OR IGNORE INTO group_changes (account_id, group_key, change_type, changed_utc, members) VALUES ($a, $g, $t, $u, $m)",
                    ("$a", accountId), ("$g", group), ("$t", type), ("$u", Stamp(utc)), ("$m", (object?)members ?? DBNull.Value));
            tx.Commit();
        }
        progress?.Invoke("People, profile pictures and calls");
        SavePeople(accountId, db.People());
        SaveCalls(accountId, db.Calls());
        NameGroupSenders(accountId);
        return results;
    }

    /// <summary>
    /// Puts a name on every group message whose sender was only a number or a
    /// code: the name saved in the address book for that WhatsApp id (or its
    /// anonymous @lid twin), else the name the person gave themselves ("~ name",
    /// decoded when WhatsApp stored it encoded), else the number. Returns the
    /// messages renamed. Run after every import; one-to-one chats already
    /// carry the contact's name.
    /// </summary>
    public int NameGroupSenders(long accountId)
    {
        using var conn = Connect();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, name, push, lid) in Query(conn, "SELECT key, name, push_name, lid FROM people WHERE account_id = $a",
                     r => (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)),
                     ("$a", accountId)))
        {
            var shown = !string.IsNullOrWhiteSpace(name) ? name.Trim() : !string.IsNullOrWhiteSpace(push) ? "~ " + push.Trim() : null;
            if (shown is null) continue;
            // An address-book name beats a self-chosen one wherever both reach the same id.
            if (!names.TryGetValue(key, out var had) || had.StartsWith('~')) names[key] = shown;
            if (lid is not null && (!names.TryGetValue(lid, out had) || had.StartsWith('~'))) names[lid] = shown;
        }
        var senders = Query(conn, """
            SELECT m.sender, m.sender_key, count(*) FROM messages m JOIN chats c ON c.id = m.chat_id
            WHERE c.account_id = $a AND c.kind = 'group' AND m.sender_key IS NOT NULL AND m.sender_key <> 'me' AND m.sender IS NOT NULL
            GROUP BY m.sender, m.sender_key
            """, r => (Sender: r.GetString(0), Key: r.GetString(1), Count: r.GetInt32(2)), ("$a", accountId));
        var renamed = 0;
        using var tx = conn.BeginTransaction();
        foreach (var (sender, key, count) in senders)
        {
            var looksBare = sender.StartsWith('+') || WhatsAppIosDatabase.IsEncodedProfile(sender, out _);
            var better = names.GetValueOrDefault(key)
                         ?? (looksBare ? WhatsAppIosDatabase.PushName(sender) : null)
                         ?? (looksBare && !sender.TrimStart('+').All(char.IsDigit) ? "+" + key.Split('@')[0] : null);
            // A name already there stays unless the address book has one for that id.
            if (better is null || better == sender || (!looksBare && (better.StartsWith('~') || !names.ContainsKey(key)))) continue;
            renamed += count;
            Exec(conn, """
                UPDATE messages SET sender = $n WHERE sender_key = $k AND sender = $s
                  AND chat_id IN (SELECT id FROM chats WHERE account_id = $a AND kind = 'group')
                """, ("$n", better), ("$s", sender), ("$k", key), ("$a", accountId));
        }
        tx.Commit();
        return renamed;
    }

    private HashSet<string> ExcludedKeys(long accountId)
    {
        using var conn = Connect();
        return Query(conn, "SELECT source_key FROM excluded_chats WHERE account_id = $a", r => r.GetString(0), ("$a", accountId))
            .ToHashSet(StringComparer.Ordinal);
    }

    private void SaveGroup(long chatId, IosGroup group)
    {
        using var conn = Connect();
        using var tx = conn.BeginTransaction();
        Exec(conn, "UPDATE chats SET created_utc = $c, creator_key = $k WHERE id = $id",
            ("$c", group.CreatedUtc is { } c ? Stamp(c) : DBNull.Value), ("$k", (object?)group.Creator ?? DBNull.Value), ("$id", chatId));
        foreach (var m in group.Members)
            Exec(conn, """
                INSERT INTO chat_members (chat_id, member_key, name, admin, active) VALUES ($c, $k, $n, $a, $v)
                ON CONFLICT (chat_id, member_key) DO UPDATE SET name = coalesce(excluded.name, name), admin = excluded.admin, active = excluded.active
                """, ("$c", chatId), ("$k", m.Jid), ("$n", (object?)m.Name ?? DBNull.Value), ("$a", m.Admin ? 1 : 0), ("$v", m.Active ? 1 : 0));
        tx.Commit();
    }

    private void SavePeople(long accountId, IReadOnlyList<IosPerson> people)
    {
        using var conn = Connect();
        using var tx = conn.BeginTransaction();
        foreach (var p in people)
        {
            string? picture = null;
            if (p.PictureSource is { } file && File.Exists(file))
            {
                using var stream = File.OpenRead(file);
                picture = StoreSmall(stream, "people", ".jpg");
            }
            Exec(conn, """
                INSERT INTO people (account_id, key, name, push_name, phone, phone_label, about, business, lid, username, picture)
                VALUES ($a, $k, $n, $pn, $ph, $pl, $ab, $bu, $li, $us, $pi)
                ON CONFLICT (account_id, key) DO UPDATE SET
                  name = coalesce(excluded.name, name), push_name = coalesce(excluded.push_name, push_name), phone = coalesce(excluded.phone, phone),
                  phone_label = coalesce(excluded.phone_label, phone_label), about = coalesce(excluded.about, about),
                  business = coalesce(excluded.business, business), lid = coalesce(excluded.lid, lid), username = coalesce(excluded.username, username),
                  picture = coalesce(excluded.picture, picture)
                """,
                ("$a", accountId), ("$k", p.Jid), ("$n", (object?)p.Name ?? DBNull.Value), ("$pn", (object?)p.PushName ?? DBNull.Value),
                ("$ph", (object?)p.Phone ?? DBNull.Value), ("$pl", (object?)p.PhoneLabel ?? DBNull.Value), ("$ab", (object?)p.About ?? DBNull.Value),
                ("$bu", (object?)p.Business ?? DBNull.Value), ("$li", (object?)p.Lid ?? DBNull.Value), ("$us", (object?)p.Username ?? DBNull.Value),
                ("$pi", (object?)picture ?? DBNull.Value));
        }
        tx.Commit();
    }

    private void SaveCalls(long accountId, IReadOnlyList<IosCall> calls)
    {
        using var conn = Connect();
        using var tx = conn.BeginTransaction();
        foreach (var c in calls)
            Exec(conn, """
                INSERT OR IGNORE INTO calls (account_id, source_id, started_utc, seconds, video, incoming, missed, outcome, group_key, participants, creator_key)
                VALUES ($a, $s, $t, $d, $v, $i, $m, $o, $g, $p, $c)
                """,
                ("$a", accountId), ("$s", c.Id), ("$t", Stamp(c.Utc)), ("$d", c.Seconds), ("$v", c.Video ? 1 : 0), ("$i", c.Incoming ? 1 : 0),
                ("$m", c.Missed ? 1 : 0), ("$o", (object?)c.Outcome ?? DBNull.Value), ("$g", (object?)c.Group ?? DBNull.Value),
                ("$p", (object?)c.Participants ?? DBNull.Value), ("$c", (object?)c.Creator ?? DBNull.Value));
        tx.Commit();
    }

    public sealed record ArchivedCall(DateTime Started, double Seconds, bool Video, bool Incoming, bool Missed, string? Group, string? Participants);

    public IReadOnlyList<ArchivedCall> Calls(long accountId)
    {
        using var conn = Connect();
        return Query(conn, "SELECT started_utc, seconds, video, incoming, missed, group_key, participants FROM calls WHERE account_id = $a ORDER BY started_utc",
            r => new ArchivedCall(ShownTime(r.GetString(0)), r.GetDouble(1), r.GetInt64(2) == 1, r.GetInt64(3) == 1, r.GetInt64(4) == 1,
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)), ("$a", accountId));
    }


    /// <summary>
    /// WhatsApp re-keys chats now and then (phone numbers moving to @lid ids).
    /// A chat already in the archive is the same chat under a new id only when
    /// its old id is gone from the phone and nearly every message sampled from
    /// the new one is already in it. Message ids alone are not enough: a
    /// broadcast carries the same id into every chat it reaches.
    /// </summary>
    private static long? FindRekeyedChat(SqliteConnection conn, long accountId, string source,
        IReadOnlyList<ParsedMessage> messages, IReadOnlySet<string>? liveKeys)
    {
        var sample = messages.Where(m => m.SourceId is not null).Select(m => m.SourceId!).Distinct().Take(50).ToList();
        if (sample.Count == 0) return null;
        var hits = new Dictionary<long, int>();
        foreach (var id in sample)
        {
            foreach (var chat in Query(conn, """
                         SELECT DISTINCT m.chat_id FROM messages m JOIN chats c ON c.id = m.chat_id
                         WHERE c.account_id = $a AND c.source = $s AND m.source_id = $i
                         """, r => r.GetInt64(0), ("$a", accountId), ("$s", source), ("$i", id)))
                hits[chat] = hits.GetValueOrDefault(chat) + 1;
        }
        foreach (var (chat, count) in hits.OrderByDescending(h => h.Value))
        {
            if (count < Math.Ceiling(sample.Count * 0.8)) return null;
            var key = Query(conn, "SELECT source_key FROM chats WHERE id = $c", r => r.IsDBNull(0) ? null : r.GetString(0), ("$c", chat)).FirstOrDefault();
            if (key is not null && liveKeys is not null && liveKeys.Contains(key)) continue; // that chat is still on the phone
            return chat;
        }
        return null;
    }

    /// <summary>Hashes a file into the store; the same content already there is reused.</summary>
    private (long Id, bool IsNew) StoreFile(SqliteConnection conn, Stream source, string name, string? storedAs = null)
    {
        Directory.CreateDirectory(FilesRoot);
        var temp = Path.Combine(FilesRoot, $".incoming-{Guid.NewGuid():N}");
        string sha;
        long size;
        try
        {
            using (var output = File.Create(temp))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    output.Write(buffer, 0, read);
                }
                sha = Convert.ToHexStringLower(hasher.GetHashAndReset());
                size = output.Length;
            }

            var existing = Query(conn, "SELECT id, ext, reduced, ifnull(mark, '') FROM files WHERE sha256 = $h",
                r => (Id: r.GetInt64(0), Ext: r.GetString(1), Reduced: r.IsDBNull(2) ? null : r.GetString(2), Mark: r.GetString(3)), ("$h", sha));
            if (existing.Count > 0)
            {
                // A file let go as irrelevant and later marked important comes
                // back whole when an import brings the original again.
                var e = existing[0];
                if (e.Reduced is not null && e.Mark == Important)
                {
                    var back = StorePath(sha, e.Ext);
                    Directory.CreateDirectory(Path.GetDirectoryName(back)!);
                    if (!File.Exists(back)) File.Move(temp, back);
                    Exec(conn, "UPDATE files SET reduced = NULL, reduced_at = NULL WHERE id = $f", ("$f", e.Id));
                }
                return (e.Id, false);
            }

            var ext = SafeExtension(storedAs) ?? SafeExtension(name) ?? "";
            var target = StorePath(sha, ext);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.Move(temp, target);
            var id = Scalar(conn, "INSERT INTO files (sha256, size, ext, name) VALUES ($h, $s, $e, $n) RETURNING id",
                ("$h", sha), ("$s", size), ("$e", ext), ("$n", name));
            return (id, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>
    /// The extension the stored file gets: letters and digits only. A
    /// document's name ("notes. final?") is no safe source for a file name.
    /// </summary>
    private static string? SafeExtension(string? name)
    {
        if (name is null) return null;
        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1) return null;
        var ext = name[(dot + 1)..];
        return ext.Length <= 8 && ext.All(char.IsAsciiLetterOrDigit) ? "." + ext.ToLowerInvariant() : null;
    }

    public string StorePath(string sha256, string ext) => Path.Combine(FilesRoot, sha256[..2], sha256 + ext);

    // ----- Reading -----

    public IReadOnlyList<Chat> Chats(long accountId)
    {
        using var conn = Connect();
        return Query(conn, """
            SELECT c.id, c.account_id, c.source, c.title, c.thread_id, count(m.id),
                   min(coalesce(m.sent_utc, m.sent_at)), max(coalesce(m.sent_utc, m.sent_at)), c.kind, c.noisy
            FROM chats c LEFT JOIN messages m ON m.chat_id = c.id
            WHERE c.account_id = $a GROUP BY c.id ORDER BY max(coalesce(m.sent_utc, m.sent_at)) DESC
            """,
            r => new Chat(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetInt64(4),
                r.GetInt32(5), r.IsDBNull(6) ? null : Parse(r.GetString(6)), r.IsDBNull(7) ? null : Parse(r.GetString(7)),
                r.IsDBNull(8) ? null : r.GetString(8), r.GetInt64(9) == 1),
            ("$a", accountId));
    }

    /// <summary>The account's chats with every joined thread folded into one entry, latest first.</summary>
    public IReadOnlyList<Conversation> Conversations(long accountId)
    {
        var chats = Chats(accountId);
        using var conn = Connect();
        var threads = Query(conn, "SELECT id, title FROM threads WHERE account_id = $a", r => (Id: r.GetInt64(0), Title: r.GetString(1)), ("$a", accountId))
            .ToDictionary(t => t.Id, t => t.Title);

        var list = new List<Conversation>();
        foreach (var c in chats.Where(c => c.ThreadId is null))
            list.Add(new Conversation(c.Id, null, c.Title, c.MessageCount, c.First, c.Last, [c.Id]));
        foreach (var group in chats.Where(c => c.ThreadId is not null).GroupBy(c => c.ThreadId!.Value))
        {
            var members = group.ToList();
            list.Add(new Conversation(null, group.Key, threads.GetValueOrDefault(group.Key, members[0].Title),
                members.Sum(c => c.MessageCount), members.Min(c => c.First), members.Max(c => c.Last),
                members.Select(c => c.Id).ToList()));
        }
        return list.OrderByDescending(c => c.Last ?? DateTime.MinValue).ToList();
    }

    /// <summary>The messages of one or more chats as a single stream, oldest first.</summary>
    public IReadOnlyList<StoredMessage> Messages(IReadOnlyList<long> chatIds)
    {
        if (chatIds.Count == 0) return [];
        using var conn = Connect();
        var ids = string.Join(",", chatIds.Select(i => i.ToString(CultureInfo.InvariantCulture)));
        return Query(conn, $"""
            SELECT m.id, m.chat_id, m.sent_at, m.sender, m.body, m.kind, m.attachment_name, m.file_id, f.sha256, f.ext, f.size,
                   m.sent_utc, m.forwarded, f.reduced, f.thumb, f.text,
                   m.starred, q.sender, q.body, q.attachment_name, m.link_title, m.link_url, m.thumb, m.vcard, m.forward_score,
                   m.sender_key, m.media_deleted, m.duration
            FROM messages m LEFT JOIN files f ON f.id = m.file_id
            LEFT JOIN messages q ON m.reply_to IS NOT NULL AND q.chat_id = m.chat_id AND q.source_id IN (m.reply_to || '|0', m.reply_to || '|1')
            WHERE m.chat_id IN ({ids}) ORDER BY coalesce(m.sent_utc, m.sent_at), m.chat_id, m.seq
            """,
            r => new StoredMessage(r.GetInt64(0), r.GetInt64(1),
                r.IsDBNull(11) ? Parse(r.GetString(2)) : ShownTime(r.GetString(11)),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4), KindFrom(r.GetString(5)), r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetInt64(7),
                r.IsDBNull(8) || !r.IsDBNull(13) ? null : StorePath(r.GetString(8), r.GetString(9)),
                r.IsDBNull(10) ? null : r.GetInt64(10),
                r.GetInt64(12) == 1,
                r.IsDBNull(13) ? null : r.GetString(13),
                r.IsDBNull(14) ? null : Path.Combine(Root, r.GetString(14)),
                r.IsDBNull(15) ? null : r.GetString(15),
                r.GetInt64(16) == 1,
                r.IsDBNull(17) ? null : r.GetString(17),
                r.IsDBNull(18) ? null : (r.GetString(18).Length > 0 ? r.GetString(18) : r.IsDBNull(19) ? null : r.GetString(19)),
                r.IsDBNull(20) ? null : r.GetString(20),
                r.IsDBNull(21) ? null : r.GetString(21),
                r.IsDBNull(22) ? null : Path.Combine(Root, r.GetString(22)),
                r.IsDBNull(23) ? null : r.GetString(23),
                r.IsDBNull(24) ? null : r.GetInt32(24),
                r.IsDBNull(25) ? null : r.GetString(25),
                r.GetInt64(26) == 1,
                r.IsDBNull(27) ? null : r.GetDouble(27)));
    }

    // ----- Managing -----

    /// <summary>
    /// Joins chats of one account into a thread read as one stream: a contact's
    /// old number and new number, say. The chats stay as they were imported, and
    /// <see cref="Unjoin"/> parts them again.
    /// </summary>
    public long Join(IReadOnlyList<long> chatIds, string title)
    {
        if (chatIds.Count < 2) throw new ArgumentException("Joining takes two chats or more.", nameof(chatIds));
        using var conn = Connect();
        using var tx = conn.BeginTransaction();
        var ids = string.Join(",", chatIds.Select(i => i.ToString(CultureInfo.InvariantCulture)));
        var accounts = Query(conn, $"SELECT DISTINCT account_id FROM chats WHERE id IN ({ids})", r => r.GetInt64(0));
        if (accounts.Count != 1)
            throw new InvalidOperationException("Only chats of the same account can be joined; accounts are never mixed.");

        // Joining a thread into another folds both into one.
        var oldThreads = Query(conn, $"SELECT DISTINCT thread_id FROM chats WHERE id IN ({ids}) AND thread_id IS NOT NULL", r => r.GetInt64(0));
        var threadId = Scalar(conn, "INSERT INTO threads (account_id, title) VALUES ($a, $t) RETURNING id", ("$a", accounts[0]), ("$t", title.Trim()));
        foreach (var old in oldThreads)
            Exec(conn, "UPDATE chats SET thread_id = $n WHERE thread_id = $o", ("$n", threadId), ("$o", old));
        Exec(conn, $"UPDATE chats SET thread_id = $n WHERE id IN ({ids})", ("$n", threadId));
        foreach (var old in oldThreads)
            Exec(conn, "DELETE FROM threads WHERE id = $o", ("$o", old));
        tx.Commit();
        return threadId;
    }

    public void Unjoin(long threadId)
    {
        using var conn = Connect();
        using var tx = conn.BeginTransaction();
        Exec(conn, "UPDATE chats SET thread_id = NULL WHERE thread_id = $t", ("$t", threadId));
        Exec(conn, "DELETE FROM threads WHERE id = $t", ("$t", threadId));
        tx.Commit();
    }

    /// <summary>
    /// Deletes chats and their messages. A stored file goes too once no other
    /// message, in any chat or account, still refers to it. Returns the bytes freed.
    /// </summary>
    public long DeleteChats(IReadOnlyList<long> chatIds)
    {
        if (chatIds.Count == 0) return 0;
        using var conn = Connect();
        var ids = string.Join(",", chatIds.Select(i => i.ToString(CultureInfo.InvariantCulture)));
        List<(long Id, string Sha, string Ext, long Size)> orphans;
        using (var tx = conn.BeginTransaction())
        {
            var threads = Query(conn, $"SELECT DISTINCT thread_id FROM chats WHERE id IN ({ids}) AND thread_id IS NOT NULL", r => r.GetInt64(0));
            // A chat deleted on purpose stays deleted: a later import leaves it out.
            Exec(conn, $"""
                INSERT OR REPLACE INTO excluded_chats (account_id, source_key, title, excluded_at)
                SELECT account_id, source_key, title, $n FROM chats WHERE id IN ({ids}) AND source_key IS NOT NULL
                """, ("$n", Now()));
            Exec(conn, $"DELETE FROM chats WHERE id IN ({ids})");
            // A thread left with one chat is no longer a join.
            foreach (var t in threads)
            {
                if (Scalar(conn, "SELECT count(*) FROM chats WHERE thread_id = $t", ("$t", t)) < 2)
                {
                    Exec(conn, "UPDATE chats SET thread_id = NULL WHERE thread_id = $t", ("$t", t));
                    Exec(conn, "DELETE FROM threads WHERE id = $t", ("$t", t));
                }
            }
            // A file marked important outlives its chats: it is never deleted with them.
            orphans = Query(conn, "SELECT f.id, f.sha256, f.ext, f.size FROM files f WHERE ifnull(f.mark, '') <> 'important' AND NOT EXISTS (SELECT 1 FROM messages m WHERE m.file_id = f.id)",
                r => (r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3))).ToList();
            foreach (var o in orphans)
                Exec(conn, "DELETE FROM files WHERE id = $f", ("$f", o.Id));
            tx.Commit();
        }

        long freed = 0;
        foreach (var o in orphans)
        {
            var path = StorePath(o.Sha, o.Ext);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    freed += o.Size;
                }
            }
            catch (IOException)
            {
                // The row is gone; a file left behind is harmless and found by its hash later.
            }
        }
        return freed;
    }

    // ----- Helpers -----

    private static string Key(string sentAt, string sender, string body, string attachment, string kind)
        => string.Join('\u001f', sentAt, sender, body, attachment, kind);

    private static string Stamp(DateTime t) => t.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
    private static DateTime Parse(string s) => DateTime.ParseExact(s, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>A stored UTC time as this computer shows it, whichever zone it is in today.</summary>
    private static DateTime ShownTime(string utc)
        => DateTime.SpecifyKind(DateTime.SpecifyKind(Parse(utc), DateTimeKind.Utc).ToLocalTime(), DateTimeKind.Unspecified);
    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string KindName(MessageKind k) => k switch
    {
        MessageKind.System => "system",
        MessageKind.MediaOmitted => "media-omitted",
        _ => "text",
    };

    private static MessageKind KindFrom(string s) => s switch
    {
        "system" => MessageKind.System,
        "media-omitted" => MessageKind.MediaOmitted,
        _ => MessageKind.Text,
    };

    private static void Exec(SqliteConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in parameters) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in parameters) cmd.Parameters.AddWithValue(n, v);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static List<T> Query<T>(SqliteConnection conn, string sql, Func<SqliteDataReader, T> map, params (string Name, object Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in parameters) cmd.Parameters.AddWithValue(n, v);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }
}
