using Microsoft.Data.Sqlite;
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

    public WhatsAppIosDatabaseTests()
    {
        Directory.CreateDirectory(Path.Combine(Container, "Message", "Media", "5511@s.whatsapp.net", "a", "b"));
        File.WriteAllBytes(Path.Combine(Container, "Message", "Media", "5511@s.whatsapp.net", "a", "b", "photo-1.jpg"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(Container, "Message", "Media", "5511@s.whatsapp.net", "a", "b", "doc-1.pdf"), [5, 6, 7]);

        using var conn = new SqliteConnection($"Data Source={Path.Combine(Container, "ChatStorage.sqlite")};Pooling=False");
        conn.Open();
        Exec(conn, """
            CREATE TABLE ZWACHATSESSION (Z_PK INTEGER PRIMARY KEY, ZCONTACTJID TEXT, ZPARTNERNAME TEXT, ZSESSIONTYPE INTEGER, ZLASTMESSAGEDATE REAL);
            CREATE TABLE ZWAGROUPMEMBER (Z_PK INTEGER PRIMARY KEY, ZCONTACTNAME TEXT, ZFIRSTNAME TEXT, ZMEMBERJID TEXT);
            CREATE TABLE ZWAMEDIAITEM (Z_PK INTEGER PRIMARY KEY, ZMEDIALOCALPATH TEXT, ZTITLE TEXT, ZLATITUDE REAL, ZLONGITUDE REAL);
            CREATE TABLE ZWAMESSAGE (Z_PK INTEGER PRIMARY KEY, ZCHATSESSION INTEGER, ZMESSAGEDATE REAL, ZISFROMME INTEGER, ZMESSAGETYPE INTEGER,
              ZTEXT TEXT, ZFROMJID TEXT, ZPUSHNAME TEXT, ZGROUPMEMBER INTEGER, ZMEDIAITEM INTEGER);

            -- Two contacts saved under one name, and a status feed that is left out.
            INSERT INTO ZWACHATSESSION VALUES (1, '5511@s.whatsapp.net', 'Ana', 0, 700000100);
            INSERT INTO ZWACHATSESSION VALUES (2, '24499@s.whatsapp.net', 'Ana', 0, 600000000);
            INSERT INTO ZWACHATSESSION VALUES (3, 'status@broadcast', 'Status', 3, 700000000);

            INSERT INTO ZWAMEDIAITEM VALUES (1, 'Media/5511@s.whatsapp.net/a/b/photo-1.jpg', 'O jardim', 0, 0);
            INSERT INTO ZWAMEDIAITEM VALUES (2, 'Media/5511@s.whatsapp.net/a/b/doc-1.pdf', NULL, 0, 0);
            INSERT INTO ZWAMEDIAITEM VALUES (3, NULL, NULL, 0, 0);

            INSERT INTO ZWAMESSAGE VALUES (1, 1, 700000000, 0, 0, 'Bom dia!', '5511@s.whatsapp.net', NULL, NULL, NULL);
            INSERT INTO ZWAMESSAGE VALUES (2, 1, 700000010, 1, 0, 'Oi', NULL, NULL, NULL, NULL);
            INSERT INTO ZWAMESSAGE VALUES (3, 1, 700000020, 0, 1, NULL, '5511@s.whatsapp.net', NULL, NULL, 1);
            INSERT INTO ZWAMESSAGE VALUES (4, 1, 700000030, 1, 8, 'Notes 1/2. final?', NULL, NULL, NULL, 2);
            INSERT INTO ZWAMESSAGE VALUES (5, 1, 700000040, 0, 2, NULL, '5511@s.whatsapp.net', NULL, NULL, 3);
            INSERT INTO ZWAMESSAGE VALUES (6, 1, 700000050, 0, 14, NULL, '5511@s.whatsapp.net', NULL, NULL, NULL);
            INSERT INTO ZWAMESSAGE VALUES (7, 2, 600000000, 0, 0, 'Número antigo', '24499@s.whatsapp.net', NULL, NULL, NULL);
            INSERT INTO ZWAMESSAGE VALUES (8, 3, 700000000, 0, 0, 'a status', NULL, NULL, NULL, NULL);
            """);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Reads_chats_and_messages_with_captions_documents_and_missing_media()
    {
        var db = WhatsAppIosDatabase.Open(_dir);
        var chats = db.Chats();
        Assert.Equal(2, chats.Count); // the status feed is not a chat
        var ana = chats.Single(c => c.Jid == "5511@s.whatsapp.net");

        var messages = db.ReadMessages(ana, "Tiago");

        Assert.Equal(6, messages.Count);
        Assert.Equal(("Ana", "Bom dia!"), (messages[0].Sender, messages[0].Body));
        Assert.Equal("Tiago", messages[1].Sender);
        Assert.Equal(("photo-1.jpg", "O jardim"), (messages[2].AttachmentName, messages[2].Body));
        Assert.Equal("Notes 1-2. final?", messages[3].AttachmentName); // a document keeps its name, made path-safe
        Assert.Equal(MessageKind.MediaOmitted, messages[4].Kind);    // a video the phone never downloaded
        Assert.Equal(MessageKind.System, messages[5].Kind);
        Assert.Equal(new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(700000000).ToLocalTime(), messages[0].SentAt); // Core Data seconds, shown locally
    }

    [Fact]
    public void Imports_contacts_of_one_name_apart_and_adds_only_what_is_new()
    {
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");

        var first = library.ImportWhatsAppIos(me.Id, Container);
        var again = library.ImportWhatsAppIos(me.Id, Container, ["+5511"]);

        Assert.Equal(2, first.Count);
        Assert.Contains(first, r => r.Title == "Ana (+5511)");
        Assert.Contains(first, r => r.Title == "Ana (+24499)");
        Assert.Equal(2, first.Sum(r => r.FilesStored));
        Assert.Equal(0, Assert.Single(again).MessagesAdded);

        var pdf = library.Files(me.Id).Single(f => f.Name.StartsWith("Notes"));
        Assert.EndsWith(".pdf", pdf.Path); // the stored file takes WhatsApp's extension, not the name's
        Assert.True(File.Exists(pdf.Path));
    }
}
