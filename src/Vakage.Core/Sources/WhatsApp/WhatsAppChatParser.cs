using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Vakage.Core.Sources.WhatsApp;

/// <summary>What a line of an exported chat turned out to be.</summary>
public enum MessageKind
{
    /// <summary>A message someone wrote, with or without an attached file.</summary>
    Text,

    /// <summary>A line WhatsApp wrote itself: encryption notice, someone joined, a subject changed.</summary>
    System,

    /// <summary>A file the export left out ("&lt;Media omitted&gt;", "imagem ocultada").</summary>
    MediaOmitted,
}

/// <summary>One message read from an exported chat, before it enters the library.</summary>
/// <param name="SentAt">Wall-clock time as the phone showed it; exports carry no time zone.</param>
/// <param name="Sender">The name as the exporting phone saved it; null for a system line.</param>
/// <param name="Body">The text, with the attachment marker removed (a caption, or empty).</param>
/// <param name="AttachmentName">The file name the export refers to, when the message carried a file.</param>
/// <param name="AttachmentSource">Where the source keeps that file, when it is not the name itself (a path inside a phone backup).</param>
/// <param name="SourceId">The source's own id for the message, unique in its chat; what a later import matches on.</param>
/// <param name="SentUtc">The exact time in UTC, when the source knows it (a phone's database does; an export does not).</param>
/// <param name="SenderKey">Who sent it, as a stable id ("me", or a WhatsApp id), whatever name they are saved under.</param>
/// <param name="Forwarded">WhatsApp marked it forwarded: someone else's content passed along.</param>
/// <param name="MediaType">image, video, audio, sticker, gif or document, for an attachment.</param>
public sealed record ParsedMessage(
    DateTime SentAt,
    string? Sender,
    string Body,
    string? AttachmentName,
    MessageKind Kind,
    string? AttachmentSource = null,
    string? SourceId = null,
    DateTime? SentUtc = null,
    string? SenderKey = null,
    bool Forwarded = false,
    string? MediaType = null,
    MessageExtras? Extras = null);

/// <summary>What a phone's database knows about a message beyond its text and file.</summary>
public sealed record MessageExtras(
    string? ReplyTo = null,
    string? ReplySender = null,
    bool Starred = false,
    int? ForwardScore = null,
    string? LinkUrl = null,
    string? LinkTitle = null,
    string? LinkSnippet = null,
    string? VCard = null,
    string? Mime = null,
    string? ThumbSource = null,
    string? OriginalSender = null,
    string? Mentions = null,
    byte[]? RawMetadata = null,
    byte[]? RawReceipts = null,
    double? Duration = null,
    long? MediaSize = null);

public static class MediaTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image", [".jpeg"] = "image", [".png"] = "image", [".heic"] = "image", [".bmp"] = "image",
        [".webp"] = "sticker", [".gif"] = "gif",
        [".mp4"] = "video", [".mov"] = "video", [".3gp"] = "video", [".avi"] = "video", [".mkv"] = "video",
        [".opus"] = "audio", [".ogg"] = "audio", [".m4a"] = "audio", [".mp3"] = "audio", [".aac"] = "audio", [".wav"] = "audio", [".caf"] = "audio",
    };

    /// <summary>The kind of a file by its name, for sources that say nothing more (an export).</summary>
    public static string FromName(string name) => ByExtension.GetValueOrDefault(Path.GetExtension(name), "document");
}

/// <summary>
/// Reads the text file WhatsApp writes when a chat is exported, from Android
/// (<c>12/03/2020 14:22 - Name: text</c>) and from iPhone
/// (<c>[12/03/2020, 14:22:05] Name: text</c>), in any of the languages the
/// phone may be set to. A line that does not start with a date continues the
/// message above it. The order of day and month is worked out from the whole
/// file, since a single line like 03/04/2020 cannot tell.
/// </summary>
public static class WhatsAppChatParser
{
    // Android: "12/03/2020 14:22 - ", "3/12/20, 2:22 PM - ", "12.03.20, 14:22 - "
    private static readonly Regex AndroidHeader = new(
        @"^(?<a>\d{1,2})[./-](?<b>\d{1,2})[./-](?<y>\d{2,4}),? (?<h>\d{1,2})[:.](?<min>\d{2})(?:[:.](?<s>\d{2}))?(?: ?(?<ampm>[AaPp]\.? ?[Mm]\.?))? - (?<rest>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // iPhone: "[12/03/2020, 14:22:05] ", "[3/12/20, 2:22:05 PM] "
    private static readonly Regex IosHeader = new(
        @"^\[(?<a>\d{1,2})[./-](?<b>\d{1,2})[./-](?<y>\d{2,4}),? (?<h>\d{1,2})[:.](?<min>\d{2})(?:[:.](?<s>\d{2}))?(?: ?(?<ampm>[AaPp]\.? ?[Mm]\.?))?\] (?<rest>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Android: "IMG-20200312-WA0001.jpg (file attached)"
    private static readonly Regex AndroidAttachment = new(
        @"^(?<file>[^\n]+?\.[A-Za-z0-9]{1,6}) \((?:file attached|arquivo anexado|archivo adjunto|Datei angehängt|fichier joint|file allegato|bestand bijgevoegd)\)(?:\n(?<caption>.*))?$",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // iPhone: "<attached: 00000012-PHOTO-2020-03-12-14-22-05.jpg>"
    private static readonly Regex IosAttachment = new(
        @"<(?:attached|anexado|adjunto|Anhang|pièce jointe|allegato|bijlage): (?<file>[^>]+)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Omitted = new(
        @"^<?(?:media omitted|mídia oculta|arquivo de mídia oculto|multimedia omitido|medien ausgeschlossen|médias omis|media omessi)>?$" +
        @"|^(?:image|video|audio|sticker|GIF|document|contact card|imagem|vídeo|áudio|figurinha|documento|cartão de contato|imagen|archivo|Bild|Audio|Video) (?:omitted|ocultad[oa]|omitid[oa]|weggelassen)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private sealed class Pending
    {
        public required string A, B, Y, H, Min;
        public string? S, AmPm;
        public required StringBuilder Rest;
    }

    public static IReadOnlyList<ParsedMessage> Parse(string text) => Parse(text, knownFiles: null);

    /// <param name="knownFiles">
    /// The files beside the chat in the export, by name. A message whose whole
    /// text is one of these names is that file, as some exports write it bare.
    /// </param>
    public static IReadOnlyList<ParsedMessage> Parse(string text, ISet<string>? knownFiles)
    {
        var pending = new List<Pending>();
        Pending? current = null;

        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = Clean(raw);
            var m = IosHeader.Match(line);
            if (!m.Success) m = AndroidHeader.Match(line);
            if (m.Success)
            {
                current = new Pending
                {
                    A = m.Groups["a"].Value,
                    B = m.Groups["b"].Value,
                    Y = m.Groups["y"].Value,
                    H = m.Groups["h"].Value,
                    Min = m.Groups["min"].Value,
                    S = m.Groups["s"].Success ? m.Groups["s"].Value : null,
                    AmPm = m.Groups["ampm"].Success ? m.Groups["ampm"].Value : null,
                    Rest = new StringBuilder(m.Groups["rest"].Value),
                };
                pending.Add(current);
            }
            else if (current is not null)
            {
                current.Rest.Append('\n').Append(line);
            }
            // Text before the first dated line (a stray BOM line) is dropped.
        }

        var dayFirst = DayComesFirst(pending);
        var result = new List<ParsedMessage>(pending.Count);
        foreach (var p in pending)
        {
            if (!TryTime(p, dayFirst, out var sentAt)) continue;
            result.Add(Classify(sentAt, p.Rest.ToString().TrimEnd('\n'), knownFiles));
        }
        return result;
    }

    /// <summary>
    /// True when the first number of a date is the day. A value above 12 in
    /// either place settles it; with none, an AM/PM clock means an American
    /// phone (month first), and a 24-hour clock means day first.
    /// </summary>
    private static bool DayComesFirst(List<Pending> lines)
    {
        foreach (var p in lines)
        {
            if (int.Parse(p.A, CultureInfo.InvariantCulture) > 12) return true;
            if (int.Parse(p.B, CultureInfo.InvariantCulture) > 12) return false;
        }
        return !lines.Any(p => p.AmPm is not null);
    }

    private static bool TryTime(Pending p, bool dayFirst, out DateTime sentAt)
    {
        sentAt = default;
        var a = int.Parse(p.A, CultureInfo.InvariantCulture);
        var b = int.Parse(p.B, CultureInfo.InvariantCulture);
        var year = int.Parse(p.Y, CultureInfo.InvariantCulture);
        if (year < 100) year += 2000;
        var hour = int.Parse(p.H, CultureInfo.InvariantCulture);
        var minute = int.Parse(p.Min, CultureInfo.InvariantCulture);
        var second = p.S is null ? 0 : int.Parse(p.S, CultureInfo.InvariantCulture);
        if (p.AmPm is { } ampm)
        {
            var pm = ampm.StartsWith('p') || ampm.StartsWith('P');
            if (hour == 12) hour = 0;
            if (pm) hour += 12;
        }
        var (day, month) = dayFirst ? (a, b) : (b, a);
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
        if (hour > 23 || minute > 59 || second > 59) return false;
        sentAt = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        return true;
    }

    private static ParsedMessage Classify(DateTime sentAt, string rest, ISet<string>? knownFiles)
    {
        // "Name: text". A system line has no sender; the colon search stops at
        // the first line so a message body holding a colon is not split.
        var firstLineEnd = rest.IndexOf('\n');
        var firstLine = firstLineEnd < 0 ? rest : rest[..firstLineEnd];
        var colon = firstLine.IndexOf(": ", StringComparison.Ordinal);
        if (colon <= 0)
            return new ParsedMessage(sentAt, null, rest, null, MessageKind.System);

        var sender = rest[..colon].Trim();
        var body = Clean(rest[(colon + 2)..]);

        if (Omitted.IsMatch(body.Trim()))
            return new ParsedMessage(sentAt, sender, "", null, MessageKind.MediaOmitted);

        var ios = IosAttachment.Match(body);
        if (ios.Success)
        {
            var caption = (body[..ios.Index] + body[(ios.Index + ios.Length)..]).Trim();
            var file = ios.Groups["file"].Value.Trim();
            return new ParsedMessage(sentAt, sender, caption, file, MessageKind.Text, MediaType: MediaTypes.FromName(file));
        }

        var android = AndroidAttachment.Match(body);
        if (android.Success)
        {
            var caption = android.Groups["caption"].Success ? android.Groups["caption"].Value.Trim() : "";
            var file = android.Groups["file"].Value.Trim();
            return new ParsedMessage(sentAt, sender, caption, file, MessageKind.Text, MediaType: MediaTypes.FromName(file));
        }

        if (knownFiles is not null && !body.Contains('\n') && knownFiles.Contains(body.Trim()))
            return new ParsedMessage(sentAt, sender, "", body.Trim(), MessageKind.Text, MediaType: MediaTypes.FromName(body.Trim()));

        return new ParsedMessage(sentAt, sender, body, null, MessageKind.Text);
    }

    /// <summary>Drops the direction marks and odd spaces WhatsApp scatters through a line.</summary>
    private static string Clean(string s)
    {
        if (s.Length == 0) return s;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '‎' or '‏' or '﻿' or '‪' or '‬':
                    continue;
                case ' ' or ' ':
                    sb.Append(' ');
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }
}
