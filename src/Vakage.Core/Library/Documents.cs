using System.Globalization;
using System.Text;

namespace Vakage.Core.Library;

/// <summary>
/// What kind of document a text is, from its words: the first kind whose
/// words it carries enough of. Portuguese and English, the languages of the
/// chats; a kind is a hint for the person, never a verdict on its own.
/// </summary>
public static class Documents
{
    public const string Receipt = "receipt";
    public const string Bill = "bill";
    public const string Ticket = "ticket";
    public const string Identity = "identity";
    public const string Legal = "legal";
    public const string Medical = "medical";
    public const string School = "school";
    public const string Text = "text";

    private static readonly (string Kind, int Needed, string[] Words)[] Kinds =
    [
        (Identity, 2, ["passaporte", "passport", "carteira de identidade", "registro geral", "cpf", "data de nascimento", "date of birth",
            "nacionalidade", "nationality", "naturalidade", "filiacao", "orgao expedidor", "visto", "visa"]),
        (Legal, 2, ["contrato", "contract", "procuracao", "certidao", "cartorio", "clausula", "outorgante", "testemunha", "escritura",
            "agreement", "notary", "assinatura", "signature", "registro civil"]),
        (Medical, 2, ["exame", "laudo", "paciente", "patient", "crm", "hospital", "clinica", "receituario", "diagnostico", "resultado",
            "medico", "doctor", "prescription", "vacina", "vaccine"]),
        (School, 2, ["escola", "school", "boletim", "matricula", "aluno", "aluna", "student", "professor", "teacher", "turma", "semestre"]),
        (Ticket, 2, ["cartao de embarque", "boarding pass", "boarding", "voo", "flight", "e-ticket", "localizador", "booking", "reserva",
            "check-in", "assento", "seat", "gate", "portao", "passageiro", "passenger"]),
        (Bill, 2, ["boleto", "fatura", "vencimento", "codigo de barras", "linha digitavel", "invoice", "due date", "conta de luz", "conta de agua"]),
        (Receipt, 2, ["comprovante", "recibo", "receipt", "pagamento", "payment", "transferencia", "transfer", "pix", "nota fiscal",
            "cupom fiscal", "danfe", "autenticacao", "total", "valor", "amount", "r$", "us$", "nt$", "debito", "credito", "aprovado", "approved"]),
    ];

    /// <summary>The kind of document the text is, or "text" for words of no known kind, or null for too few words.</summary>
    public static string? Classify(string? text, int minimumWords = 12)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var plain = Fold(text);
        foreach (var (kind, needed, words) in Kinds)
            if (words.Count(w => plain.Contains(w, StringComparison.Ordinal)) >= needed)
                return kind;
        return Thumbnails.Words(text) >= minimumWords ? Text : null;
    }

    /// <summary>Lower case without accents, so "Comprovante de transferência" meets "transferencia".</summary>
    internal static string Fold(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }

    public static string Describe(string kind) => kind switch
    {
        Receipt => "a payment receipt",
        Bill => "a bill",
        Ticket => "a ticket or booking",
        Identity => "an identity document",
        Legal => "a legal document",
        Medical => "a medical document",
        School => "a school document",
        _ => "a document",
    };
}
