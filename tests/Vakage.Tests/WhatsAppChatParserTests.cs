using Vakage.Core.Sources.WhatsApp;

namespace Vakage.Tests;

public class WhatsAppChatParserTests
{
    [Fact]
    public void Reads_an_Android_export_in_Portuguese()
    {
        var text = """
            12/03/2020 14:22 - As mensagens e as chamadas são protegidas com a criptografia de ponta a ponta.
            12/03/2020 14:22 - Ana: Bom dia!
            12/03/2020 14:23 - Bruno: Oi, tudo bem?
            Segunda linha da mesma mensagem.
            13/03/2020 09:01 - Ana: IMG-20200313-WA0001.jpg (arquivo anexado)
            Foto do jardim
            13/03/2020 09:02 - Bruno: <Mídia oculta>
            """;

        var messages = WhatsAppChatParser.Parse(text);

        Assert.Equal(5, messages.Count);
        Assert.Equal(MessageKind.System, messages[0].Kind);
        Assert.Null(messages[0].Sender);
        Assert.Equal("Ana", messages[1].Sender);
        Assert.Equal(new DateTime(2020, 3, 12, 14, 22, 0), messages[1].SentAt);
        Assert.Equal("Oi, tudo bem?\nSegunda linha da mesma mensagem.", messages[2].Body);
        Assert.Equal("IMG-20200313-WA0001.jpg", messages[3].AttachmentName);
        Assert.Equal("Foto do jardim", messages[3].Body);
        Assert.Equal(MessageKind.MediaOmitted, messages[4].Kind);
    }

    [Fact]
    public void Reads_an_iPhone_export_with_a_twelve_hour_clock()
    {
        var text = "‎[3/12/20, 2:22:05 PM] Ana: Messages and calls are end-to-end encrypted.\n" +
                   "[3/12/20, 2:23:10 PM] Bruno: ‎<attached: 00000012-PHOTO-2020-03-12-14-23-10.jpg>\n" +
                   "[3/13/20, 12:05:00 AM] Ana: After midnight\n";

        var messages = WhatsAppChatParser.Parse(text);

        Assert.Equal(3, messages.Count);
        Assert.Equal(new DateTime(2020, 3, 12, 14, 22, 5), messages[0].SentAt);
        Assert.Equal("00000012-PHOTO-2020-03-12-14-23-10.jpg", messages[1].AttachmentName);
        Assert.Equal("", messages[1].Body);
        Assert.Equal(new DateTime(2020, 3, 13, 0, 5, 0), messages[2].SentAt);
    }

    [Fact]
    public void Tells_day_first_from_month_first_by_the_whole_file()
    {
        // 03/04 alone is ambiguous; 25/04 later settles it as day first.
        var dayFirst = "03/04/2021 10:00 - Ana: um\n25/04/2021 10:00 - Ana: dois\n";
        Assert.Equal(new DateTime(2021, 4, 3, 10, 0, 0), WhatsAppChatParser.Parse(dayFirst)[0].SentAt);

        var monthFirst = "03/04/21, 10:00 AM - Ana: one\n04/25/21, 10:00 AM - Ana: two\n";
        Assert.Equal(new DateTime(2021, 3, 4, 10, 0, 0), WhatsAppChatParser.Parse(monthFirst)[0].SentAt);
    }

    [Fact]
    public void A_bare_file_name_is_an_attachment_when_the_export_ships_that_file()
    {
        var text = "12/03/2020 14:22 - Ana: PTT-20200312-WA0003.opus\n12/03/2020 14:23 - Ana: notes.txt is a name\n";
        var known = new HashSet<string> { "PTT-20200312-WA0003.opus" };

        var messages = WhatsAppChatParser.Parse(text, known);

        Assert.Equal("PTT-20200312-WA0003.opus", messages[0].AttachmentName);
        Assert.Null(messages[1].AttachmentName);
    }

    [Theory]
    [InlineData("WhatsApp Chat with Ana Lima.txt", "Ana Lima")]
    [InlineData("Conversa do WhatsApp com Família.txt", "Família")]
    [InlineData("_chat.txt", "Chat")]
    public void Names_the_chat_from_its_file(string file, string title)
    {
        Assert.Equal(title, WhatsAppExport.TitleFor(file, file));
    }
}
