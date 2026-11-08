using System.Globalization;

namespace Vakage.Core.Library;

/// <summary>Which files a list shows, by their standing (the person's mark, else Vakage's suggestion).</summary>
public enum FileList
{
    All,
    Important,
    Irrelevant,
    Unsorted,
    /// <summary>PDFs and other documents, and pictures whose text reads as a document.</summary>
    Documents,
}

/// <summary>What the messages and the file itself say about a file: what the suggestion rules weigh.</summary>
public sealed record FileFacts(
    string Media,
    int Chats = 1,
    bool Channel = false,
    bool Forwarded = false,
    bool OwnFromMe = false,
    bool AllPerson = false,
    bool CloseChat = false,
    double Shape = 0,
    int LongSide = 0,
    bool NoisyFromOthers = false,
    int? OcrWords = null,
    string? DocKind = null,
    double AgeYears = 0,
    int ForwardScore = 0)
{
    /// <summary>The proportions a phone camera takes (4:3, 16:9) at a camera's size, not a meme's.</summary>
    public bool CameraLike => Media == "image" && LongSide >= 1000 && (Shape is >= 1.30 and <= 1.36 || Shape is >= 1.74 and <= 1.80);
}

public sealed record SuggestionResult(int Important, int Irrelevant, int Unsorted);

public sealed record ReduceResult(int ToText, int ToThumbnail, int Removed, int Kept, long BytesFreed, IReadOnlyList<string> Notes);

/// <summary>
/// Sorting the files of an archive into what is precious and what is noise,
/// and letting the noise go. Every file stands on one of three lists:
/// important, irrelevant, or unsorted. Vakage suggests a standing from what
/// it knows (forwarded, a sticker, sent around many chats, an own photo in a
/// close one-to-one chat); the person's own mark always wins, and a
/// suggestion never overrides it. Reducing an irrelevant file keeps its text
/// (an image that is only words, a PDF) or a small thumbnail, and deletes the
/// original; a file marked important is never reduced and never deleted.
/// </summary>
public sealed partial class ArchiveLibrary
{
    public const string Important = "important";
    public const string Irrelevant = "irrelevant";

    /// <summary>A person chat counts as close from this many messages on.</summary>
    public const int CloseChatMessages = 100;

    /// <summary>A picture with this many words or more is kept as its text alone.</summary>
    public const int TextPictureWords = 20;

    public string ThumbsRoot => Path.Combine(Root, "thumbs");

    /// <summary>Long side over short side from which a picture is taken for a screenshot.</summary>
    public const double ScreenshotShape = 1.9;

    /// <summary>Reads the size of every picture not measured yet, from its header alone.</summary>
    private void MeasurePictures()
    {
        using var conn = Connect();
        var pending = Query(conn, """
            SELECT f.id, f.sha256, f.ext FROM files f
            WHERE f.width IS NULL AND f.reduced IS NULL AND f.ext IN ('.jpg', '.jpeg', '.png', '.webp', '.gif', '.bmp')
            """, r => (Id: r.GetInt64(0), Sha: r.GetString(1), Ext: r.GetString(2)));
        if (pending.Count == 0) return;
        using var tx = conn.BeginTransaction();
        foreach (var p in pending)
        {
            var (w, h) = (0, 0);
            try
            {
                using var codec = SkiaSharp.SKCodec.Create(StorePath(p.Sha, p.Ext));
                if (codec is not null) (w, h) = (codec.Info.Width, codec.Info.Height);
            }
            catch (Exception)
            {
                // Unreadable: measured as 0 x 0, so it is not tried again.
            }
            Exec(conn, "UPDATE files SET width = $w, height = $h WHERE id = $f", ("$w", w), ("$h", h), ("$f", p.Id));
        }
        tx.Commit();
    }

    /// <summary>
    /// Every file of an account, largest first. With a sender, only the files
    /// that person sent (<paramref name="excludeSender"/> turns that around);
    /// <paramref name="list"/> keeps one standing.
    /// </summary>
    public IReadOnlyList<StoredFile> Files(long accountId, string? sender = null, bool excludeSender = false, FileList list = FileList.All)
    {
        using var conn = Connect();
        var filter = sender is null ? "" : excludeSender
            ? "AND f.id NOT IN (SELECT m2.file_id FROM messages m2 JOIN chats c2 ON c2.id = m2.chat_id WHERE c2.account_id = $a AND m2.sender = $s AND m2.file_id IS NOT NULL)"
            : "AND f.id IN (SELECT m2.file_id FROM messages m2 JOIN chats c2 ON c2.id = m2.chat_id WHERE c2.account_id = $a AND m2.sender = $s AND m2.file_id IS NOT NULL)";
        filter += list switch
        {
            FileList.Documents => " AND (f.doc_kind IS NOT NULL AND f.doc_kind <> 'text' OR f.ext IN ('.pdf', '.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx', '.odt', '.txt', '.rtf'))",
            FileList.Important => " AND coalesce(f.mark, f.suggestion) = 'important'",
            FileList.Irrelevant => " AND coalesce(f.mark, f.suggestion) = 'irrelevant'",
            FileList.Unsorted => " AND coalesce(f.mark, f.suggestion) IS NULL",
            _ => "",
        };
        var parameters = new List<(string, object)> { ("$a", accountId) };
        if (sender is not null) parameters.Add(("$s", sender));
        return Query(conn, $"""
            SELECT f.id, f.sha256, f.size, f.name, f.ext,
                   group_concat(DISTINCT ifnull(m.sender, '')), group_concat(DISTINCT c.title), min(coalesce(m.sent_utc, m.sent_at)), count(m.id),
                   ifnull(max(m.media_type), 'document'), f.mark, f.suggestion, f.suggestion_reason, f.reduced, f.thumb, f.text
            FROM files f JOIN messages m ON m.file_id = f.id JOIN chats c ON c.id = m.chat_id
            WHERE c.account_id = $a {filter}
            GROUP BY f.id ORDER BY (f.reduced IS NULL) DESC, f.size DESC, f.id
            """,
            r => new StoredFile(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetString(3), StorePath(r.GetString(1), r.GetString(4)),
                (r.IsDBNull(5) ? "" : r.GetString(5)).Replace(",", ", "), (r.IsDBNull(6) ? "" : r.GetString(6)).Replace(",", ", "),
                DateTime.ParseExact(r.GetString(7), "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), r.GetInt32(8),
                r.GetString(9), Str(r, 10), Str(r, 11), Str(r, 12), Str(r, 13),
                Str(r, 14) is { } thumb ? Path.Combine(Root, thumb) : null, Str(r, 15)),
            parameters.ToArray());
    }

    private static string? Str(Microsoft.Data.Sqlite.SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    /// <summary>The person's decision: important, irrelevant, or null to leave it to the suggestion.</summary>
    public void Mark(IReadOnlyList<long> fileIds, string? mark)
    {
        if (mark is not (null or Important or Irrelevant)) throw new ArgumentException("A mark is important, irrelevant or none.", nameof(mark));
        using var conn = Connect();
        using var tx = conn.BeginTransaction();
        foreach (var id in fileIds)
            Exec(conn, "UPDATE files SET mark = $m WHERE id = $f", ("$m", (object?)mark ?? DBNull.Value), ("$f", id));
        tx.Commit();
    }

    /// <summary>
    /// Works out a suggested standing for every file from what the messages
    /// carrying it say. Run after every import; it never touches a mark.
    /// </summary>
    public SuggestionResult Suggest()
    {
        MeasurePictures();
        using var conn = Connect();
        var rows = Query(conn, $"""
            WITH sizes AS (SELECT chat_id, count(*) AS n FROM messages GROUP BY chat_id)
            SELECT f.id,
                   ifnull(max(m.media_type), 'document'),
                   count(DISTINCT m.chat_id),
                   max(c.kind = 'channel'),
                   max(m.forwarded),
                   min(m.forwarded),
                   max(m.sender_key = 'me' AND m.forwarded = 0),
                   min(ifnull(c.kind, 'person') = 'person'),
                   max(ifnull(c.kind, 'person') = 'person' AND s.n >= {CloseChatMessages}),
                   f.width, f.height,
                   max(c.noisy = 1 AND ifnull(m.sender_key, '') <> 'me'), f.ocr_words, f.doc_kind,
                   min(coalesce(m.sent_utc, m.sent_at)), max(ifnull(m.forward_score, 0))
            FROM files f JOIN messages m ON m.file_id = f.id JOIN chats c ON c.id = m.chat_id JOIN sizes s ON s.chat_id = m.chat_id
            GROUP BY f.id
            """,
            r =>
            {
                long w = r.IsDBNull(9) ? 0 : r.GetInt64(9), h = r.IsDBNull(10) ? 0 : r.GetInt64(10);
                var first = DateTime.ParseExact(r.GetString(14), "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
                return (Id: r.GetInt64(0), Facts: new FileFacts(
                    Media: r.GetString(1),
                    Chats: r.GetInt32(2),
                    Channel: r.GetInt64(3) == 1,
                    Forwarded: r.GetInt64(5) == 1,
                    OwnFromMe: !r.IsDBNull(6) && r.GetInt64(6) == 1,
                    AllPerson: r.GetInt64(7) == 1,
                    CloseChat: r.GetInt64(8) == 1,
                    Shape: w == 0 || h == 0 ? 0 : (double)Math.Max(w, h) / Math.Min(w, h),
                    LongSide: (int)Math.Max(w, h),
                    NoisyFromOthers: !r.IsDBNull(11) && r.GetInt64(11) == 1,
                    OcrWords: r.IsDBNull(12) ? null : r.GetInt32(12),
                    DocKind: r.IsDBNull(13) ? null : r.GetString(13),
                    AgeYears: (DateTime.UtcNow - first).TotalDays / 365.25,
                    ForwardScore: r.GetInt32(15)));
            });

        int important = 0, irrelevant = 0, unsorted = 0;
        using var tx = conn.BeginTransaction();
        foreach (var f in rows)
        {
            var (standing, reason) = SuggestFor(f.Facts);
            Exec(conn, "UPDATE files SET suggestion = $s, suggestion_reason = $r WHERE id = $f",
                ("$s", (object?)standing ?? DBNull.Value), ("$r", (object?)reason ?? DBNull.Value), ("$f", f.Id));
            if (standing == Important) important++;
            else if (standing == Irrelevant) irrelevant++;
            else unsorted++;
        }
        tx.Commit();
        return new SuggestionResult(important, irrelevant, unsorted);
    }

    /// <summary>A shorthand for tests and callers with only a few facts.</summary>
    internal static (string? Standing, string? Reason) SuggestFor(string media, int chats, bool channel, bool forwarded,
        bool ownFromMe, bool allPerson, bool closeChat, double shape = 0)
        => SuggestFor(new FileFacts(media, chats, channel, forwarded, ownFromMe, allPerson, closeChat, shape));

    /// <summary>The rules, in order; the first that applies decides. Explainable on purpose.</summary>
    public static (string? Standing, string? Reason) SuggestFor(FileFacts f)
    {
        var visual = f.Media is "image" or "video";
        if (f.Media == "sticker") return (Irrelevant, "a sticker");
        if (f.Media == "gif") return (Irrelevant, "a GIF");
        if (f.Channel) return (Irrelevant, "from a channel");
        if ((visual || f.Media == "audio") && f.Forwarded)
            return (Irrelevant, f.ForwardScore >= 5 ? "forwarded many times" : "forwarded: someone else's, passed along");

        // Documents, from the words read in them (a PDF, or a photo of a paper).
        if (f.DocKind is { } kind && kind != Documents.Text && (f.Media == "document" || f.Media == "image"))
        {
            var what = Documents.Describe(kind);
            var year = DateTime.UtcNow.AddDays(-f.AgeYears * 365.25).Year;
            switch (kind)
            {
                case Documents.Receipt or Documents.Bill when f.AgeYears >= 1:
                    return (Irrelevant, $"{what} from {year}: its text is enough");
                case Documents.Ticket when f.AgeYears >= 1:
                    return (Irrelevant, $"{what} from {year}: its text is enough");
                case Documents.Receipt or Documents.Bill or Documents.Ticket:
                    return (null, $"{what}, still recent");
                default:
                    return (Important, what);
            }
        }
        if (f.Media == "image" && f.OcrWords >= TextPictureWords && f.NoisyFromOthers)
            return (Irrelevant, "a picture of words, in a noisy chat");

        if (visual && f.Chats >= 3) return (Irrelevant, $"sent around {f.Chats} chats");

        // A noisy chat (a relative's daily forwards): pruned hard, but a real
        // photo, of the camera's shape and without words, may be the family's.
        if (f.NoisyFromOthers)
        {
            if (f.Media == "audio") return (Important, "a voice note");
            if (f.Media == "video") return (null, "a video in a noisy chat: worth a look");
            if (f.Media == "image")
                return f.CameraLike && (f.OcrWords ?? 0) < 5
                    ? (Important, "looks like a camera photo, in a noisy chat")
                    : (Irrelevant, f.OcrWords > 0 ? "a picture with words, in a noisy chat" : "everyday noise, in a noisy chat");
        }

        // A camera takes 4:3 or 16:9; a phone's screen is about 9:19.5. A
        // screenshot is rarely precious and rarely junk: left for the person.
        if (f.Media == "image" && f.Shape >= ScreenshotShape) return (null, "shaped like a screenshot");
        if (f.Media == "image" && f.OcrWords >= TextPictureWords) return (null, "a picture of words");
        if (visual && f.OwnFromMe) return (Important, f.Media == "video" ? "a video of your own" : "a photo of your own");
        if (f.AllPerson && f.CloseChat)
        {
            if (f.Media == "audio") return (Important, "a voice note in a close one-to-one chat");
            if (visual) return (Important, f.Media == "video" ? "a video sent in a close one-to-one chat" : "a photo sent in a close one-to-one chat");
        }
        if (f.Media == "document") return (null, "a document");
        return (null, null);
    }

    /// <summary>Marks chats as noisy (a relative's daily forwards): their files are pruned hard.</summary>
    public void SetNoisy(IReadOnlyList<long> chatIds, bool noisy)
    {
        using var conn = Connect();
        foreach (var id in chatIds)
            Exec(conn, "UPDATE chats SET noisy = $n WHERE id = $c", ("$n", noisy ? 1 : 0), ("$c", id));
    }

    /// <summary>
    /// Reads the words in the account's pictures and PDFs that have not been
    /// read yet, several at a time, and keeps them with each file with the kind
    /// of document they make. Nothing is deleted. Returns how many were read.
    /// </summary>
    public int ReadTexts(long accountId, IReadOnlyList<long>? chatIds, ITextReader reader, int jobs = 6, Action<string>? progress = null,
        CancellationToken cancel = default)
    {
        if (reader.Unavailable is { } why) throw new InvalidOperationException(why);
        List<(long Id, string Path)> pending;
        using (var conn = Connect())
        {
            var chatFilter = chatIds is { Count: > 0 } ? $"AND m.chat_id IN ({string.Join(",", chatIds)})" : "";
            pending = Query(conn, $"""
                SELECT DISTINCT f.id, f.sha256, f.ext FROM files f JOIN messages m ON m.file_id = f.id JOIN chats c ON c.id = m.chat_id
                WHERE c.account_id = $a {chatFilter} AND f.ocr IS NULL AND f.reduced IS NULL
                  AND (f.ext = '.pdf' OR (f.ext IN ('.jpg', '.jpeg', '.png') AND ifnull(m.media_type, 'image') = 'image'))
                """, r => (r.GetInt64(0), StorePath(r.GetString(1), r.GetString(2))), ("$a", accountId));
        }
        var results = new System.Collections.Concurrent.ConcurrentQueue<(long Id, string Text)>();
        var done = 0;
        void Flush()
        {
            if (results.IsEmpty) return;
            using var conn = Connect();
            using var tx = conn.BeginTransaction();
            while (results.TryDequeue(out var r))
                Exec(conn, "UPDATE files SET ocr = $t, ocr_words = $w, doc_kind = $k WHERE id = $f",
                    ("$t", r.Text), ("$w", Thumbnails.Words(r.Text)), ("$k", (object?)Documents.Classify(r.Text) ?? DBNull.Value), ("$f", r.Id));
            tx.Commit();
        }
        Parallel.ForEach(pending, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs), CancellationToken = cancel }, p =>
        {
            if (!File.Exists(p.Path)) return;
            var text = reader.Read(p.Path);
            if (text is null) return; // unreadable now: tried again next time
            results.Enqueue((p.Id, text));
            var n = Interlocked.Increment(ref done);
            if (n % 100 == 0)
            {
                lock (results) Flush();
                progress?.Invoke($"{n:N0}/{pending.Count:N0} read");
            }
        });
        lock (results) Flush();
        return done;
    }

    /// <summary>
    /// Ties together the account's one-to-one chats saved under one name with
    /// different numbers ("Ana (+5511…)", "Ana (+24499…)"): the same person,
    /// each history kept apart, readable alone or all together. A chat already
    /// tied to others brings its group along. Returns the groups made or grown.
    /// </summary>
    public int GroupSameNames(long accountId)
    {
        var chats = Chats(accountId).Where(c => (c.Kind ?? "person") == "person").ToList();
        var groups = 0;
        foreach (var family in chats.Where(c => c.Title.Contains(" (+")).GroupBy(c => c.Title[..c.Title.IndexOf(" (+", StringComparison.Ordinal)]))
        {
            var members = family.ToList();
            if (members.Count < 2 || members.Select(m => m.ThreadId).Distinct().Count() == 1 && members[0].ThreadId is not null) continue;
            Join(members.Select(m => m.Id).ToList(), family.Key);
            groups++;
        }
        return groups;
    }

    /// <summary>
    /// Lets irrelevant files go. Of each one only what matters is kept: the
    /// words of a picture that is mostly text, or of a PDF; a small thumbnail of
    /// any other picture or video; nothing but a notice for the rest. The
    /// original is then deleted from the store. A file marked important, or not
    /// standing as irrelevant, is left alone, as is anything that could not be
    /// read or thumbnailed safely. With <paramref name="fileIds"/> null, every
    /// irrelevant file of the account.
    /// </summary>
    public ReduceResult Reduce(long accountId, IReadOnlyList<long>? fileIds, ITextReader? reader, Action<string>? progress = null)
    {
        var candidates = Files(accountId, list: FileList.Irrelevant)
            .Where(f => f.Reduced is null && f.Mark != Important && (fileIds is null || fileIds.Contains(f.Id)))
            .ToList();
        int toText = 0, toThumb = 0, removed = 0, kept = 0, n = 0;
        long freed = 0;
        var notes = new List<string>();
        var canRead = reader is not null && reader.Unavailable is null;

        foreach (var f in candidates)
        {
            progress?.Invoke($"{++n}/{candidates.Count}  {f.Name}");
            if (!File.Exists(f.Path))
            {
                kept++;
                continue;
            }
            var ext = Path.GetExtension(f.Path).ToLowerInvariant();
            string? reduced = null, thumb = null, text = null;

            if (f.MediaType is "image" or "sticker" or "gif")
            {
                var words = canRead && f.MediaType == "image" ? reader!.Read(f.Path) : null;
                if (words is not null && Thumbnails.Words(words) >= TextPictureWords)
                {
                    reduced = "text";
                    text = words;
                }
                else if (Thumbnails.FromImage(f.Path, ThumbPathFor(f.Sha256)))
                {
                    reduced = "thumbnail";
                    thumb = ThumbRelative(f.Sha256);
                    text = words is { Length: > 0 } ? words : null;
                }
            }
            else if (f.MediaType == "video")
            {
                if (Thumbnails.FromVideo(f.Path, ThumbPathFor(f.Sha256)))
                {
                    reduced = "thumbnail";
                    thumb = ThumbRelative(f.Sha256);
                }
                else
                {
                    reduced = "removed";
                }
            }
            else if (ext == ".pdf")
            {
                var words = canRead ? reader!.Read(f.Path) : null;
                words = string.IsNullOrWhiteSpace(words) ? null : words;
                if (words is not null && words.Count(char.IsLetter) >= 80)
                {
                    reduced = "text";
                    text = words;
                }
            }
            else if (f.MediaType == "audio")
            {
                reduced = "removed";
            }

            if (reduced is null)
            {
                kept++;
                notes.Add($"Kept {f.Name}: nothing safe to keep in its place.");
                continue;
            }

            using (var conn = Connect())
                Exec(conn, "UPDATE files SET reduced = $r, thumb = $t, text = $x, reduced_at = $n WHERE id = $f",
                    ("$r", reduced), ("$t", (object?)thumb ?? DBNull.Value), ("$x", (object?)text ?? DBNull.Value), ("$n", Now()), ("$f", f.Id));
            try
            {
                File.Delete(f.Path);
                freed += f.Size;
            }
            catch (IOException ex)
            {
                notes.Add($"{f.Name} is marked reduced, but its original could not be deleted yet: {ex.Message}");
            }

            switch (reduced)
            {
                case "text": toText++; break;
                case "thumbnail": toThumb++; break;
                default: removed++; break;
            }
        }
        if (reader?.Unavailable is { } why) notes.Insert(0, why);
        return new ReduceResult(toText, toThumb, removed, kept, freed, notes);
    }

    /// <summary>Reads a file's text and keeps it beside the file, which stays (a PDF worth searching, say).</summary>
    public string? ExtractText(long fileId, ITextReader reader)
    {
        using var conn = Connect();
        var row = Query(conn, "SELECT sha256, ext FROM files WHERE id = $f AND reduced IS NULL", r => (r.GetString(0), r.GetString(1)), ("$f", fileId));
        if (row.Count == 0) return null;
        var text = reader.Read(StorePath(row[0].Item1, row[0].Item2));
        if (text is not null)
            Exec(conn, "UPDATE files SET text = $x WHERE id = $f", ("$x", text), ("$f", fileId));
        return text;
    }

    private string ThumbPathFor(string sha256) => Path.Combine(Root, ThumbRelative(sha256));

    private static string ThumbRelative(string sha256) => Path.Combine("thumbs", sha256[..2], sha256 + ".jpg");
}
