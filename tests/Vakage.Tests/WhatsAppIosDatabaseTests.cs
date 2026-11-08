using Microsoft.Data.Sqlite;
using SkiaSharp;
using Vakage.Core.Library;
using Vakage.Core.Sources.WhatsApp;

namespace Vakage.Tests;

/// <summary>
/// A made-up iPhone WhatsApp container: the few tables and columns Vakage
/// reads from ChatStorage.sqlite, and a Message\Media folder.
/// </summary>
public sealed class WhatsAppIosDatabaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vakage-ios-" + Guid.NewGuid().ToString("N"));
    private string Container => Path.Combine(_dir, "group.net.whatsapp.WhatsApp.shared");
    private string Media => Path.Combine(Container, "Message", "Media", "5511@s.whatsapp.net", "a", "b");

    public WhatsAppIosDatabaseTests()
    {
        Directory.CreateDirectory(Media);
        WritePicture(Path.Combine(Media, "photo-1.jpg"), 800, 600);
        File.WriteAllBytes(Path.Combine(Media, "doc-1.pdf"), [5, 6, 7]);
        WritePicture(Path.Combine(Media, "meme.jpg"), 640, 640, SKColors.Gold);

        using var conn = Open();
        Exec(conn, """
            CREATE TABLE ZWACHATSESSION (Z_PK INTEGER PRIMARY KEY, ZCONTACTJID TEXT, ZPARTNERNAME TEXT, ZSESSIONTYPE INTEGER, ZLASTMESSAGEDATE REAL, ZARCHIVED INTEGER);
            CREATE TABLE ZWABLACKLISTITEM (Z_PK INTEGER PRIMARY KEY, ZJID TEXT);
            CREATE TABLE ZWAGROUPMEMBERSCHANGE (Z_PK INTEGER PRIMARY KEY, ZCHANGETYPE INTEGER, ZCHANGEDATE REAL, ZGROUPJID TEXT, ZMEMBERJIDS TEXT);
            CREATE TABLE ZWAGROUPMEMBER (Z_PK INTEGER PRIMARY KEY, ZCONTACTNAME TEXT, ZFIRSTNAME TEXT, ZMEMBERJID TEXT,
              ZCHATSESSION INTEGER, ZISADMIN INTEGER, ZISACTIVE INTEGER);
            CREATE TABLE ZWAMEDIAITEM (Z_PK INTEGER PRIMARY KEY, ZMEDIALOCALPATH TEXT, ZTITLE TEXT, ZLATITUDE REAL, ZLONGITUDE REAL,
              ZMETADATA BLOB, ZVCARDSTRING TEXT, ZXMPPTHUMBPATH TEXT, ZMOVIEDURATION REAL, ZFILESIZE INTEGER);
            CREATE TABLE ZWAMESSAGE (Z_PK INTEGER PRIMARY KEY, ZCHATSESSION INTEGER, ZMESSAGEDATE REAL, ZISFROMME INTEGER, ZMESSAGETYPE INTEGER,
              ZTEXT TEXT, ZFROMJID TEXT, ZPUSHNAME TEXT, ZGROUPMEMBER INTEGER, ZMEDIAITEM INTEGER, ZSTANZAID TEXT, ZFLAGS INTEGER,
              ZSTARRED INTEGER, ZMESSAGEINFO INTEGER);
            CREATE TABLE ZWAMESSAGEINFO (Z_PK INTEGER PRIMARY KEY, ZMESSAGE INTEGER, ZRECEIPTINFO BLOB);
            CREATE TABLE ZWAMESSAGEDATAITEM (Z_PK INTEGER PRIMARY KEY, ZMESSAGE INTEGER, ZINDEX INTEGER, ZTITLE TEXT, ZSUMMARY TEXT, ZCONTENT1 TEXT);
            CREATE TABLE ZWAGROUPINFO (Z_PK INTEGER PRIMARY KEY, ZCHATSESSION INTEGER, ZCREATIONDATE REAL, ZCREATORJID TEXT, ZOWNERJID TEXT);
            CREATE TABLE ZWAPROFILEPUSHNAME (Z_PK INTEGER PRIMARY KEY, ZJID TEXT, ZPUSHNAME TEXT);

            -- Two contacts saved under one name, a group, and a status feed that is left out.
            INSERT INTO ZWACHATSESSION VALUES (1, '5511@s.whatsapp.net', 'Ana', 0, 700000100, 0);
            INSERT INTO ZWACHATSESSION VALUES (2, '24499@s.whatsapp.net', 'Ana', 0, 600000000, 0);
            INSERT INTO ZWACHATSESSION VALUES (3, 'status@broadcast', 'Status', 3, 700000000, 0);
            INSERT INTO ZWACHATSESSION VALUES (4, '123-456@g.us', 'Família', 1, 700000500, 0);
            INSERT INTO ZWAGROUPMEMBER VALUES (1, 'Bruno', NULL, '5522@s.whatsapp.net', 4, 1, 1);
            INSERT INTO ZWAGROUPINFO VALUES (1, 4, 600000000, '5522@s.whatsapp.net', NULL);
            INSERT INTO ZWAPROFILEPUSHNAME VALUES (1, '5511@s.whatsapp.net', 'Aninha');

            INSERT INTO ZWAMEDIAITEM VALUES (1, 'Media/5511@s.whatsapp.net/a/b/photo-1.jpg', 'O jardim', 0, 0, NULL, NULL, NULL, NULL, NULL);
            INSERT INTO ZWAMEDIAITEM VALUES (2, 'Media/5511@s.whatsapp.net/a/b/doc-1.pdf', NULL, 0, 0, NULL, NULL, NULL, NULL, NULL);
            INSERT INTO ZWAMEDIAITEM VALUES (3, NULL, NULL, 0, 0, NULL, 'video/mp4', 'Media/5511@s.whatsapp.net/a/b/video.thumb', NULL, NULL);
            INSERT INTO ZWAMEDIAITEM VALUES (4, 'Media/5511@s.whatsapp.net/a/b/meme.jpg', NULL, 0, 0, NULL, NULL, NULL, NULL, NULL);

            INSERT INTO ZWAMESSAGE VALUES (1, 1, 700000000, 0, 0, 'Bom dia!', '5511@s.whatsapp.net', NULL, NULL, NULL, 'A1', 0, 0, NULL);
            INSERT INTO ZWAMESSAGE VALUES (2, 1, 700000010, 1, 0, 'Oi', NULL, NULL, NULL, NULL, 'A2', 0, 0, NULL);
            INSERT INTO ZWAMESSAGE VALUES (3, 1, 700000020, 0, 1, NULL, '5511@s.whatsapp.net', NULL, NULL, 1, 'A3', 0, 0, NULL);
            INSERT INTO ZWAMESSAGE VALUES (4, 1, 700000030, 1, 8, 'Notes 1/2. final?', NULL, NULL, NULL, 2, 'A4', 0, 0, NULL);
            INSERT INTO ZWAMESSAGE VALUES (5, 1, 700000040, 0, 2, NULL, '5511@s.whatsapp.net', NULL, NULL, 3, 'A5', 0, 0, NULL);
            INSERT INTO ZWAMESSAGE VALUES (6, 1, 700000050, 0, 14, NULL, '5511@s.whatsapp.net', NULL, NULL, NULL, 'A6', 0, 0, NULL);
            INSERT INTO ZWAMESSAGE VALUES (7, 2, 600000000, 0, 0, 'Número antigo', '24499@s.whatsapp.net', NULL, NULL, NULL, 'B1', 0, 0, NULL);
            INSERT INTO ZWAMEDIAITEM VALUES (10, NULL, NULL, 0, 0, X'2A02413132133535313140732E77686174736170702E6E6574', NULL, NULL, NULL, NULL);
            INSERT INTO ZWAMESSAGE VALUES (10, 1, 700000060, 1, 0, 'Respondendo', NULL, NULL, NULL, 10, 'A7', 0, 1, NULL);
            INSERT INTO ZWAMESSAGEDATAITEM VALUES (1, 10, 0, 'Um título', 'Um resumo', 'https://example.org');
            INSERT INTO ZWAMESSAGE VALUES (8, 3, 700000000, 0, 0, 'a status', NULL, NULL, NULL, NULL, 'S1', 0, 0, NULL);
            -- A forwarded picture in the group.
            INSERT INTO ZWAMESSAGE VALUES (9, 4, 700000500, 0, 1, NULL, '123-456@g.us', NULL, 1, 4, 'G1', 384, 0, NULL);
            """);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={Path.Combine(Container, "ChatStorage.sqlite")};Pooling=False");
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void WritePicture(string path, int width, int height, SKColor? color = null)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color ?? SKColors.ForestGreen);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        File.WriteAllBytes(path, data.ToArray());
    }

    private sealed class FakeReader(string text) : ITextReader
    {
        public string? Unavailable => null;
        public string? Read(string path) => text;
    }

    [Fact]
    public void Reads_chats_and_messages_with_ids_captions_documents_and_missing_media()
    {
        var db = WhatsAppIosDatabase.Open(_dir);
        var chats = db.Chats();
        Assert.Equal(3, chats.Count); // the status feed is not a chat
        Assert.Equal("group", chats.Single(c => c.Name == "Família").Kind);
        var ana = chats.Single(c => c.Jid == "5511@s.whatsapp.net");

        var messages = db.ReadMessages(ana, "Tiago");

        Assert.Equal(7, messages.Count);
        Assert.Equal(("Ana", "Bom dia!", "A1|0"), (messages[0].Sender, messages[0].Body, messages[0].SourceId));
        Assert.Equal(("Tiago", "me"), (messages[1].Sender, messages[1].SenderKey));
        Assert.Equal(("photo-1.jpg", "O jardim", "image"), (messages[2].AttachmentName, messages[2].Body, messages[2].MediaType));
        Assert.Equal("Notes 1-2. final?", messages[3].AttachmentName); // a document keeps its name, made path-safe
        Assert.Equal(MessageKind.MediaOmitted, messages[4].Kind);    // a video the phone never downloaded
        Assert.Equal(MessageKind.System, messages[5].Kind);
        Assert.Equal(new DateTime(2001, 1, 1).AddSeconds(700000000), messages[0].SentUtc);

        var group = db.ReadMessages(chats.Single(c => c.Name == "Família"), "Tiago").Single();
        Assert.Equal(("Bruno", "5522@s.whatsapp.net", true), (group.Sender, group.SenderKey, group.Forwarded));
    }

    [Fact]
    public void Imports_contacts_of_one_name_apart_and_adds_only_what_is_new()
    {
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");

        var first = library.ImportWhatsAppIos(me.Id, Container);
        var again = library.ImportWhatsAppIos(me.Id, Container, ["+5511"]);

        Assert.Equal(3, first.Count);
        Assert.Contains(first, r => r.Title == "Ana (+5511)");
        Assert.Contains(first, r => r.Title == "Ana (+24499)");
        Assert.Equal(3, first.Sum(r => r.FilesStored));
        Assert.Equal(0, Assert.Single(again).MessagesAdded);

        var pdf = library.Files(me.Id).Single(f => f.Name.StartsWith("Notes"));
        Assert.EndsWith(".pdf", pdf.Path); // the stored file takes WhatsApp's extension, not the name's
        Assert.True(File.Exists(pdf.Path));
        Assert.False(Directory.Exists(Path.Combine(library.Root, "sources"))); // nothing of WhatsApp's own is kept: all of it is in the archive
    }

    [Fact]
    public void A_backup_a_year_later_joins_on_even_after_renames_and_new_ids()
    {
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");
        library.ImportWhatsAppIos(me.Id, Container, ["5511"]);

        // A year on: the contact is renamed, WhatsApp has moved the chat to a
        // @lid id, and there is one new message.
        using (var conn = Open())
            Exec(conn, """
                UPDATE ZWACHATSESSION SET ZPARTNERNAME = 'Ana Lima', ZCONTACTJID = '9988@lid' WHERE Z_PK = 1;
                INSERT INTO ZWAMESSAGE VALUES (20, 1, 731536000, 0, 0, 'Um ano depois', '9988@lid', NULL, NULL, NULL, 'A20', 0, 0, NULL);
                """);
        SqliteConnection.ClearAllPools();

        var later = Assert.Single(library.ImportWhatsAppIos(me.Id, Container, ["Ana Lima"]));

        Assert.False(later.NewChat);
        Assert.Equal(1, later.MessagesAdded);
        var chat = Assert.Single(library.Chats(me.Id), c => c.Title == "Ana Lima");
        Assert.Equal(8, chat.MessageCount);
        Assert.Equal("Um ano depois", library.Messages([chat.Id])[^1].Body);
    }

    [Fact]
    public void Keeps_replies_stars_previews_thumbnails_people_and_groups()
    {
        File.WriteAllBytes(Path.Combine(Media, "video.thumb"), [9, 9, 9]);
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");
        var results = library.ImportWhatsAppIos(me.Id, Container);

        var ana = library.Messages([results.Single(r => r.Title == "Ana (+5511)").ChatId]);
        var reply = ana.Single(m => m.Body == "Respondendo");
        Assert.True(reply.Starred);
        Assert.Equal(("Ana", "Bom dia!"), (reply.QuotedSender, reply.QuotedBody));
        Assert.Equal(("Um título", "https://example.org"), (reply.LinkTitle, reply.LinkUrl));
        var missingVideo = ana.Single(m => m.Kind == MessageKind.MediaOmitted);
        Assert.True(File.Exists(missingVideo.MessageThumb)); // the thumbnail of a video never downloaded

        using var conn = new SqliteConnection($"Data Source={Path.Combine(library.Root, "vakage.sqlite")};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT (SELECT push_name FROM people WHERE key = '5511@s.whatsapp.net'), (SELECT count(*) FROM chat_members), (SELECT creator_key FROM chats WHERE title = 'Família')";
        using var r = cmd.ExecuteReader();
        r.Read();
        Assert.Equal(("Aninha", 1L, "5522@s.whatsapp.net"), (r.GetString(0), r.GetInt64(1), r.GetString(2)));
    }

    [Fact]
    public void A_deleted_chat_and_channels_stay_out_of_later_imports()
    {
        using (var conn = Open())
            Exec(conn, """
                INSERT INTO ZWACHATSESSION VALUES (7, 'news@newsletter', 'News', 5, 700001000, 0);
                INSERT INTO ZWAMESSAGE VALUES (50, 7, 700001000, 0, 0, 'headline', NULL, NULL, NULL, NULL, 'N1', 0, 0, NULL);
                """);
        SqliteConnection.ClearAllPools();
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");

        var first = library.ImportWhatsAppIos(me.Id, Container);
        Assert.DoesNotContain(first, r => r.Title == "News");
        library.DeleteChats([first.Single(r => r.Title == "Família").ChatId]);
        var again = library.ImportWhatsAppIos(me.Id, Container);

        Assert.DoesNotContain(again, r => r.Title == "Família");
        Assert.Contains(library.ImportWhatsAppIos(me.Id, Container, includeChannels: true), r => r.Title == "News");
    }

    [Fact]
    public void Picked_messages_can_be_deleted_lose_their_media_or_be_compressed()
    {
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");
        var ana = library.ImportWhatsAppIos(me.Id, Container).Single(r => r.Title == "Ana (+5511)").ChatId;
        var messages = library.Messages([ana]);

        // Files exchanged with one person, of one kind.
        Assert.Single(library.Files(me.Id, chatIds: [ana], mediaTypes: ["image"]));
        Assert.Single(library.Files(me.Id, chatIds: [ana], mediaTypes: ["document"]));

        // Delete a message: gone, and a later import does not bring it back.
        var hello = messages.Single(m => m.Body == "Bom dia!");
        library.DeleteMessages([hello.Id]);
        library.ImportWhatsAppIos(me.Id, Container, ["+5511"]);
        Assert.DoesNotContain(library.Messages([ana]), m => m.Body == "Bom dia!");

        // Delete only the media: the message stays, with a notice; the file goes.
        var pdf = messages.Single(m => m.AttachmentName!?.StartsWith("Notes") == true);
        library.DeleteMedia([pdf.Id]);
        var after = library.Messages([ana]).Single(m => m.Id == pdf.Id);
        Assert.True(after.MediaDeleted);
        Assert.Null(after.FileId);
        Assert.False(File.Exists(pdf.FilePath));

        // Compress chosen files whatever their standing, but never an important one.
        var photo = messages.Single(m => m.AttachmentName == "photo-1.jpg");
        var files = library.FilesOf([photo.Id]);
        Assert.Equal(1, library.Reduce(me.Id, files, null, chosen: true).ToThumbnail);
        Assert.Equal("thumbnail", library.Messages([ana]).Single(m => m.Id == photo.Id).Reduced);
    }

    [Fact]
    public void Chats_sharing_message_ids_stay_apart()
    {
        // A broadcast carries one message id into every chat it reaches.
        using (var conn = Open())
            Exec(conn, """
                INSERT INTO ZWACHATSESSION VALUES (5, 'a@newsletter', 'Channel A', 5, 700001000, 0);
                INSERT INTO ZWACHATSESSION VALUES (6, 'b@newsletter', 'Channel B', 5, 700001000, 0);
                INSERT INTO ZWAMESSAGE VALUES (40, 5, 700001000, 0, 0, 'news', NULL, NULL, NULL, NULL, 'X1', 0, 0, NULL);
                INSERT INTO ZWAMESSAGE VALUES (41, 5, 700001001, 0, 0, 'more', NULL, NULL, NULL, NULL, 'X2', 0, 0, NULL);
                INSERT INTO ZWAMESSAGE VALUES (42, 6, 700001000, 0, 0, 'news', NULL, NULL, NULL, NULL, 'X1', 0, 0, NULL);
                INSERT INTO ZWAMESSAGE VALUES (43, 6, 700001002, 0, 0, 'other', NULL, NULL, NULL, NULL, 'X3', 0, 0, NULL);
                """);
        SqliteConnection.ClearAllPools();
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");

        var results = library.ImportWhatsAppIos(me.Id, Container, ["Channel"], includeChannels: true);

        Assert.Equal(2, results.Select(r => r.ChatId).Distinct().Count());
        Assert.All(results, r => Assert.Equal(2, r.MessagesAdded));
        Assert.Equal("channel", library.Chats(me.Id).First(c => c.Title == "Channel B").Kind);
    }

    [Fact]
    public void Suggests_standings_and_a_mark_wins()
    {
        Assert.Equal(("irrelevant", "a sticker"), ArchiveLibrary.SuggestFor("sticker", 1, false, false, false, true, true));
        Assert.Equal("irrelevant", ArchiveLibrary.SuggestFor("image", 1, false, forwarded: true, false, false, false).Standing);
        Assert.Equal("irrelevant", ArchiveLibrary.SuggestFor("image", 4, false, false, false, false, false).Standing);
        Assert.Equal("important", ArchiveLibrary.SuggestFor("image", 1, false, false, ownFromMe: true, false, false).Standing);
        Assert.Equal("important", ArchiveLibrary.SuggestFor("audio", 1, false, false, false, allPerson: true, closeChat: true).Standing);
        Assert.Null(ArchiveLibrary.SuggestFor("document", 1, false, false, false, true, true).Standing);
        Assert.Equal((null, "shaped like a screenshot"), ArchiveLibrary.SuggestFor("image", 1, false, false, true, true, true, shape: 2.16));
        Assert.Equal("important", ArchiveLibrary.SuggestFor("image", 1, false, false, true, true, true, shape: 1.33).Standing);

        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");
        library.ImportWhatsAppIos(me.Id, Container);
        library.Suggest();

        var meme = Assert.Single(library.Files(me.Id, list: FileList.Irrelevant));
        Assert.Equal("meme.jpg", meme.Name);
        library.Mark([meme.Id], ArchiveLibrary.Important);
        library.Suggest();
        Assert.Empty(library.Files(me.Id, list: FileList.Irrelevant));
        Assert.Equal("important", library.Files(me.Id, list: FileList.Important).Single(f => f.Id == meme.Id).Standing);
    }

    [Fact]
    public void Reducing_keeps_a_thumbnail_or_the_text_and_deletes_the_original()
    {
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");
        library.ImportWhatsAppIos(me.Id, Container);
        library.Suggest();
        var meme = library.Files(me.Id, list: FileList.Irrelevant).Single();
        var photo = library.Files(me.Id).Single(f => f.Name == "photo-1.jpg");
        library.Mark([photo.Id], ArchiveLibrary.Irrelevant);

        var result = library.Reduce(me.Id, [meme.Id, photo.Id], new FakeReader("Feliz Natal"));

        Assert.Equal(2, result.ToThumbnail);
        Assert.False(File.Exists(meme.Path));
        var reduced = library.Files(me.Id).Single(f => f.Id == meme.Id);
        Assert.Equal("thumbnail", reduced.Reduced);
        Assert.True(File.Exists(reduced.ThumbPath));
        Assert.True(new FileInfo(reduced.ThumbPath!).Length < 20_000);

        // A picture that is mostly words keeps only its words.
        var words = string.Join(' ', Enumerable.Repeat("palavra", 30));
        WritePicture(Path.Combine(Media, "notice.jpg"), 500, 500, SKColors.White);
        using (var conn = Open())
            Exec(conn, """
                INSERT INTO ZWAMEDIAITEM VALUES (9, 'Media/5511@s.whatsapp.net/a/b/notice.jpg', NULL, 0, 0, NULL, NULL, NULL, NULL, NULL);
                INSERT INTO ZWAMESSAGE VALUES (30, 4, 700000900, 0, 1, NULL, '123-456@g.us', NULL, 1, 9, 'G9', 384, 0, NULL);
                """);
        SqliteConnection.ClearAllPools();
        library.ImportWhatsAppIos(me.Id, Container, ["Família"]);
        library.Suggest();
        var notice = library.Files(me.Id, list: FileList.Irrelevant).Single(f => f.Name == "notice.jpg");
        Assert.Equal(1, library.Reduce(me.Id, [notice.Id], new FakeReader(words)).ToText);
        var message = library.Messages(library.Chats(me.Id).Where(c => c.Title == "Família").Select(c => c.Id).ToList())[^1];
        Assert.Equal(("text", words, (string?)null), (message.Reduced, message.FileText, message.FilePath));
    }

    [Fact]
    public void An_important_file_is_never_reduced_and_outlives_its_chat()
    {
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");
        var results = library.ImportWhatsAppIos(me.Id, Container);
        var photo = library.Files(me.Id).Single(f => f.Name == "photo-1.jpg");
        library.Mark([photo.Id], ArchiveLibrary.Important);

        Assert.Equal(0, library.Reduce(me.Id, [photo.Id], null).ToThumbnail);
        library.DeleteChats([results.Single(r => r.Title == "Ana (+5511)").ChatId]);

        Assert.True(File.Exists(photo.Path));
    }
}
