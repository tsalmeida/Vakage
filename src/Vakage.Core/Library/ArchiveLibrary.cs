using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Vakage.Core.Sources.WhatsApp;

namespace Vakage.Core.Library;

public sealed record Account(long Id, string Name, string? SelfName);

/// <summary>A chat as one export (or several exports of the same chat) brought it in.</summary>
public sealed record Chat(long Id, long AccountId, string Source, string Title, long? ThreadId, int MessageCount, DateTime? First, DateTime? Last);

/// <summary>
/// What the conversation list shows: a chat on its own, or a joined thread of
/// several chats of the same account read as one stream.
/// </summary>
public sealed record Conversation(long? ChatId, long? ThreadId, string Title, int MessageCount, DateTime? First, DateTime? Last, IReadOnlyList<long> ChatIds)
{
    public bool IsJoined => ThreadId is not null;
}

public sealed record StoredMessage(long Id, long ChatId, DateTime SentAt, string? Sender, string Body, MessageKind Kind, string? AttachmentName, long? FileId, string? FilePath, long? FileSize);

/// <summary>One stored file, with who sent it and where. A file sent many times is one row.</summary>
public sealed record StoredFile(long Id, string Sha256, long Size, string Name, string Path, string Senders, string Chats, DateTime FirstSent, int References);

public sealed record ImportResult(long ChatId, string Title, bool NewChat, int MessagesAdded, int MessagesAlreadyThere, int FilesStored, int FilesAlreadyStored, int FilesMissing);

/// <summary>
/// The archive on disk: <c>vakage.sqlite</c> indexes accounts, chats and
/// messages, and <c>files\ab\&lt;sha256&gt;.ext</c> holds each attached file
/// once, by its content. Two chats, or two accounts, that carry the same photo
/// point at the same file, so a file is never stored twice however many
/// exports bring it in. Accounts never mix: every chat belongs to exactly one,
/// and a joined thread only joins chats of one account.
/// </summary>
public sealed class ArchiveLibrary
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
        }.ToString());
        conn.Open();
        return conn;
    }

    private void Migrate()
    {
        using var conn = Connect();
        Exec(conn, """
            PRAGMA journal_mode = WAL;
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
        var chatColumns = Query(conn, "SELECT name FROM pragma_table_info('chats')", r => r.GetString(0));
        if (!chatColumns.Contains("source_key"))
            Exec(conn, "ALTER TABLE chats ADD COLUMN source_key TEXT");
    }

    // ----- Accounts -----

    public IReadOnlyList<Account> Accounts()
    {
        using var conn = Connect();
        return Query(conn, "SELECT id, name, self_name FROM accounts ORDER BY name COLLATE NOCASE",
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

    /// <summary>Everyone who wrote in the account's chats, most active first.</summary>
    public IReadOnlyList<string> Senders(long accountId)
    {
        using var conn = Connect();
        return Query(conn, """
            SELECT m.sender FROM messages m JOIN chats c ON c.id = m.chat_id
            WHERE c.account_id = $a AND m.sender IS NOT NULL
            GROUP BY m.sender ORDER BY count(*) DESC
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
        IReadOnlyList<ParsedMessage> messages, Func<string, Stream?> openFile, string sourcePath)
    {
        using var conn = Connect();
        using var tx = conn.BeginTransaction();

        var chatIds = sourceKey is not null
            ? Query(conn, "SELECT id FROM chats WHERE account_id = $a AND source = $s AND source_key = $k",
                r => r.GetInt64(0), ("$a", accountId), ("$s", source), ("$k", sourceKey))
            : Query(conn, "SELECT id FROM chats WHERE account_id = $a AND source = $s AND title = $t AND source_key IS NULL",
                r => r.GetInt64(0), ("$a", accountId), ("$s", source), ("$t", title));
        var newChat = chatIds.Count == 0;
        var chatId = newChat
            ? Scalar(conn, "INSERT INTO chats (account_id, source, source_key, title, created_at) VALUES ($a, $s, $k, $t, $n) RETURNING id",
                ("$a", accountId), ("$s", source), ("$k", (object?)sourceKey ?? DBNull.Value), ("$t", title), ("$n", Now()))
            : chatIds[0];

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
            var key = Key(sentAt, m.Sender ?? "", m.Body, m.AttachmentName ?? "", kind);
            if (already.TryGetValue(key, out var count) && count > 0)
            {
                already[key] = count - 1;
                skipped++;
                continue;
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
                INSERT INTO messages (chat_id, seq, sent_at, sender, body, kind, attachment_name, file_id)
                VALUES ($c, $q, $t, $s, $b, $k, $an, $f)
                """,
                ("$c", chatId), ("$q", ++seq), ("$t", sentAt), ("$s", (object?)m.Sender ?? DBNull.Value), ("$b", m.Body),
                ("$k", kind), ("$an", (object?)m.AttachmentName ?? DBNull.Value), ("$f", (object?)fileId ?? DBNull.Value));
            added++;
        }

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
    public IReadOnlyList<ImportResult> ImportWhatsAppIos(long accountId, string path, IReadOnlyList<string>? chatFilter = null,
        Action<string>? progress = null)
    {
        var db = WhatsAppIosDatabase.Open(path);
        var account = Accounts().First(a => a.Id == accountId);
        var self = SelfNameFor(accountId) ?? account.Name;
        if (account.SelfName is null) SetSelfName(accountId, self);

        var chats = db.Chats();
        var sharedNames = chats.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var picked = chatFilter is { Count: > 0 }
            ? chats.Where(c => chatFilter.Any(f => c.Name.Contains(f, StringComparison.OrdinalIgnoreCase) || c.Number.Contains(f.TrimStart('+'), StringComparison.Ordinal))).ToList()
            : chats.ToList();

        var results = new List<ImportResult>();
        foreach (var chat in picked)
        {
            var title = sharedNames.Contains(chat.Name) && !chat.IsGroup ? $"{chat.Name} (+{chat.Number})" : chat.Name;
            progress?.Invoke($"{title}: {chat.MessageCount:N0} messages");
            results.Add(ImportMessages(accountId, "whatsapp", "ios:" + chat.Jid, title,
                db.ReadMessages(chat, self), db.OpenFile, db.Root));
        }
        return results;
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

            var existing = Query(conn, "SELECT id FROM files WHERE sha256 = $h", r => r.GetInt64(0), ("$h", sha));
            if (existing.Count > 0) return (existing[0], false);

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
            SELECT c.id, c.account_id, c.source, c.title, c.thread_id, count(m.id), min(m.sent_at), max(m.sent_at)
            FROM chats c LEFT JOIN messages m ON m.chat_id = c.id
            WHERE c.account_id = $a GROUP BY c.id ORDER BY max(m.sent_at) DESC
            """,
            r => new Chat(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetInt64(4),
                r.GetInt32(5), r.IsDBNull(6) ? null : Parse(r.GetString(6)), r.IsDBNull(7) ? null : Parse(r.GetString(7))),
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
            SELECT m.id, m.chat_id, m.sent_at, m.sender, m.body, m.kind, m.attachment_name, m.file_id, f.sha256, f.ext, f.size
            FROM messages m LEFT JOIN files f ON f.id = m.file_id
            WHERE m.chat_id IN ({ids}) ORDER BY m.sent_at, m.chat_id, m.seq
            """,
            r => new StoredMessage(r.GetInt64(0), r.GetInt64(1), Parse(r.GetString(2)), r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4), KindFrom(r.GetString(5)), r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetInt64(7),
                r.IsDBNull(8) ? null : StorePath(r.GetString(8), r.GetString(9)),
                r.IsDBNull(10) ? null : r.GetInt64(10)));
    }

    /// <summary>
    /// Every file stored for an account, largest first. With a sender, only the
    /// files that person sent; <paramref name="excludeSender"/> turns that around.
    /// </summary>
    public IReadOnlyList<StoredFile> Files(long accountId, string? sender = null, bool excludeSender = false)
    {
        using var conn = Connect();
        var filter = sender is null ? "" : excludeSender
            ? "AND f.id NOT IN (SELECT m2.file_id FROM messages m2 JOIN chats c2 ON c2.id = m2.chat_id WHERE c2.account_id = $a AND m2.sender = $s AND m2.file_id IS NOT NULL)"
            : "AND f.id IN (SELECT m2.file_id FROM messages m2 JOIN chats c2 ON c2.id = m2.chat_id WHERE c2.account_id = $a AND m2.sender = $s AND m2.file_id IS NOT NULL)";
        var parameters = new List<(string, object)> { ("$a", accountId) };
        if (sender is not null) parameters.Add(("$s", sender));
        return Query(conn, $"""
            SELECT f.id, f.sha256, f.size, f.name, f.ext,
                   group_concat(DISTINCT ifnull(m.sender, '')), group_concat(DISTINCT c.title), min(m.sent_at), count(m.id)
            FROM files f JOIN messages m ON m.file_id = f.id JOIN chats c ON c.id = m.chat_id
            WHERE c.account_id = $a {filter}
            GROUP BY f.id ORDER BY f.size DESC, f.id
            """,
            r => new StoredFile(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetString(3), StorePath(r.GetString(1), r.GetString(4)),
                (r.IsDBNull(5) ? "" : r.GetString(5)).Replace(",", ", "), (r.IsDBNull(6) ? "" : r.GetString(6)).Replace(",", ", "),
                Parse(r.GetString(7)), r.GetInt32(8)),
            parameters.ToArray());
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
            orphans = Query(conn, "SELECT f.id, f.sha256, f.ext, f.size FROM files f WHERE NOT EXISTS (SELECT 1 FROM messages m WHERE m.file_id = f.id)",
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
