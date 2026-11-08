using Vakage.Core.Library;

namespace Vakage.Tests;

public class RulesTests
{
    [Theory]
    [InlineData("Comprovante de Transferência PIX\nValor: R$ 150,00\nData 12/03/2021 Aprovado", "receipt")]
    [InlineData("CARTÃO DE EMBARQUE / BOARDING PASS  Voo LA3456  Assento 12C  Portão 5", "ticket")]
    [InlineData("REPÚBLICA FEDERATIVA DO BRASIL  PASSAPORTE  Nacionalidade BRASILEIRA  Data de nascimento", "identity")]
    [InlineData("Laudo do exame de sangue. Paciente: fulano. CRM 1234. Resultado normal.", "medical")]
    [InlineData("Bom dia! Que Deus abençoe", null)]
    public void Tells_kinds_of_documents_by_their_words(string text, string? kind)
        => Assert.Equal(kind, Documents.Classify(text));

    [Fact]
    public void Old_receipts_go_and_identity_papers_stay()
    {
        Assert.Equal("irrelevant", ArchiveLibrary.SuggestFor(new FileFacts("document", DocKind: "receipt", AgeYears: 3)).Standing);
        Assert.Null(ArchiveLibrary.SuggestFor(new FileFacts("image", DocKind: "receipt", AgeYears: 0.2)).Standing);
        Assert.Equal("important", ArchiveLibrary.SuggestFor(new FileFacts("image", DocKind: "identity", AgeYears: 6)).Standing);
    }

    [Fact]
    public void A_noisy_chat_keeps_camera_photos_and_voice_and_drops_the_rest()
    {
        var family = new FileFacts("image", AllPerson: true, CloseChat: true, Shape: 1.333, LongSide: 1600, NoisyFromOthers: true, OcrWords: 0);
        var greeting = family with { OcrWords = 6 };
        var meme = family with { Shape = 1.0, LongSide = 1080 };

        Assert.Equal("important", ArchiveLibrary.SuggestFor(family).Standing);
        Assert.Equal("irrelevant", ArchiveLibrary.SuggestFor(greeting).Standing);
        Assert.Equal("irrelevant", ArchiveLibrary.SuggestFor(meme).Standing);
        Assert.Equal("important", ArchiveLibrary.SuggestFor(new FileFacts("audio", NoisyFromOthers: true)).Standing);
        // Outside a noisy chat the same meme in a close chat is still kept for the person to judge.
        Assert.Equal("important", ArchiveLibrary.SuggestFor(meme with { NoisyFromOthers = false }).Standing);
    }
}
