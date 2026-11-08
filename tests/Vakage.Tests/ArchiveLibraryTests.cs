using System.IO.Compression;
using System.Text;
using Vakage.Core.Library;

namespace Vakage.Tests;

public sealed class ArchiveLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vakage-tests-" + Guid.NewGuid().ToString("N"));

    public ArchiveLibraryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly byte[] Photo = Encoding.ASCII.GetBytes("a photo, as bytes, sent to two chats");
    private static readonly byte[] BigVideo = new byte[50_000];

    /// <summary>An Android "Export chat, include media" zip.</summary>
    private string AndroidZip(string title, string chat, params (string Name, byte[] Bytes)[] files)
    {
        var path = Path.Combine(_dir, $"WhatsApp Chat with {title}.zip");
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var w = new StreamWriter(zip.CreateEntry($"WhatsApp Chat with {title}.txt").Open(), new UTF8Encoding(false)))
            w.Write(chat);
        foreach (var (name, bytes) in files)
        {
            using var s = zip.CreateEntry(name).Open();
            s.Write(bytes);
        }
        return path;
    }

    private const string AnaChat = """
        12/03/2020 14:22 - Tiago: Bom dia!
        12/03/2020 14:23 - Ana: IMG-20200312-WA0001.jpg (file attached)
        12/03/2020 14:24 - Ana: VID-20200312-WA0002.mp4 (file attached)
        12/03/2020 14:25 - Tiago: ok
        12/03/2020 14:25 - Tiago: ok
        """;

    private const string BrunoChat = """
        14/03/2020 10:00 - Tiago: IMG-20200314-WA0007.jpg (file attached)
        14/03/2020 10:01 - Bruno: Que foto!
        """;

    [Fact]
    public void Imports_a_chat_and_stores_each_file_once_across_chats()
    {
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");

        var ana = library.ImportWhatsApp(me.Id, AndroidZip("Ana", AnaChat,
            ("IMG-20200312-WA0001.jpg", Photo), ("VID-20200312-WA0002.mp4", BigVideo)));
        // The same photo, forwarded to Bruno under another name.
        var bruno = library.ImportWhatsApp(me.Id, AndroidZip("Bruno", BrunoChat, ("IMG-20200314-WA0007.jpg", Photo)));

        Assert.True(ana.NewChat);
        Assert.Equal(5, ana.MessagesAdded);
        Assert.Equal(2, ana.FilesStored);
        Assert.Equal(0, bruno.FilesStored);
        Assert.Equal(1, bruno.FilesAlreadyStored);

        var files = library.Files(me.Id);
        Assert.Equal(2, files.Count);
        Assert.Equal(BigVideo.Length, files[0].Size); // largest first
        Assert.Equal(2, files[1].References);
        Assert.True(File.Exists(files[1].Path));
        Assert.Single(Directory.GetFiles(library.FilesRoot, "*.jpg", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_later_export_of_the_same_chat_adds_only_what_is_new()
    {
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");
        library.ImportWhatsApp(me.Id, AndroidZip("Ana", AnaChat, ("IMG-20200312-WA0001.jpg", Photo)));

        var later = AnaChat + "\n15/03/2020 08:00 - Ana: Novidades\n";
        var again = library.ImportWhatsApp(me.Id, AndroidZip("Ana", later, ("IMG-20200312-WA0001.jpg", Photo)));

        Assert.False(again.NewChat);
        Assert.Equal(1, again.MessagesAdded);
        Assert.Equal(5, again.MessagesAlreadyThere);
        var chat = Assert.Single(library.Chats(me.Id));
        Assert.Equal(6, chat.MessageCount); // both "ok"s kept
    }

    [Fact]
    public void Filters_files_by_who_sent_them()
    {
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");
        library.ImportWhatsApp(me.Id, AndroidZip("Ana", AnaChat,
            ("IMG-20200312-WA0001.jpg", Photo), ("VID-20200312-WA0002.mp4", BigVideo)));
        library.ImportWhatsApp(me.Id, AndroidZip("Bruno", BrunoChat, ("IMG-20200314-WA0007.jpg", new byte[] { 1, 2, 3 })));

        Assert.Equal(2, library.Files(me.Id, sender: "Ana").Count);
        Assert.Single(library.Files(me.Id, sender: "Tiago"));
        Assert.Equal(2, library.Files(me.Id, sender: "Tiago", excludeSender: true).Count);
    }

    [Fact]
    public void Joined_chats_read_as_one_stream_and_part_again()
    {
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");
        var old = library.ImportWhatsApp(me.Id, AndroidZip("Ana (old number)", AnaChat));
        var current = library.ImportWhatsApp(me.Id, AndroidZip("Ana", "01/01/2021 09:00 - Ana: Número novo!\n"));

        var thread = library.Join([old.ChatId, current.ChatId], "Ana");
        var joined = Assert.Single(library.Conversations(me.Id));
        Assert.True(joined.IsJoined);
        Assert.Equal(6, joined.MessageCount);
        var stream = library.Messages(joined.ChatIds);
        Assert.Equal("Número novo!", stream[^1].Body);

        library.Unjoin(thread);
        Assert.Equal(2, library.Conversations(me.Id).Count);
    }

    [Fact]
    public void Accounts_are_never_joined_together()
    {
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var mine = library.ImportWhatsApp(library.AddAccount("Tiago").Id, AndroidZip("Ana", AnaChat));
        var hers = library.ImportWhatsApp(library.AddAccount("Ana").Id, AndroidZip("Tiago", BrunoChat));

        Assert.Throws<InvalidOperationException>(() => library.Join([mine.ChatId, hers.ChatId], "Both"));
    }

    [Fact]
    public void Deleting_a_chat_keeps_a_file_another_chat_still_uses()
    {
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");
        var ana = library.ImportWhatsApp(me.Id, AndroidZip("Ana", AnaChat,
            ("IMG-20200312-WA0001.jpg", Photo), ("VID-20200312-WA0002.mp4", BigVideo)));
        library.ImportWhatsApp(me.Id, AndroidZip("Bruno", BrunoChat, ("IMG-20200314-WA0007.jpg", Photo)));

        var freed = library.DeleteChats([ana.ChatId]);

        Assert.Equal(BigVideo.Length, freed);
        var left = Assert.Single(library.Files(me.Id));
        Assert.True(File.Exists(left.Path));
    }

    [Fact]
    public void Works_out_the_owner_from_the_sender_in_every_chat()
    {
        var library = ArchiveLibrary.Open(Path.Combine(_dir, "library"));
        var me = library.AddAccount("Tiago");
        library.ImportWhatsApp(me.Id, AndroidZip("Ana", AnaChat));
        Assert.Null(library.SelfNameFor(me.Id)); // one chat cannot tell

        library.ImportWhatsApp(me.Id, AndroidZip("Bruno", BrunoChat));
        Assert.Equal("Tiago", library.SelfNameFor(me.Id));

        library.SetSelfName(me.Id, "Eu");
        Assert.Equal("Eu", library.SelfNameFor(me.Id));
    }
}
