using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Vakage.Core.Sources.WhatsApp;

/// <summary>
/// One exported WhatsApp chat as the phone hands it over: a .zip (the usual
/// "Export chat, include media"), the folder that zip was unpacked into, or the
/// chat's .txt on its own. Nothing here writes to the export; the library copies
/// what it needs and the original can be deleted afterwards.
/// </summary>
public sealed class WhatsAppExport : IDisposable
{
    private static readonly Regex TitleFromName = new(
        @"^(?:WhatsApp Chat with|WhatsApp Chat -|Conversa do WhatsApp com|Chat de WhatsApp con|WhatsApp-Chat mit|Discussion WhatsApp avec|Chat WhatsApp con)\s*(?<title>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly ZipArchive? _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _zipEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _folderFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The chat's name: the contact or group the export was made from.</summary>
    public string Title { get; }

    /// <summary>The path the export was opened from.</summary>
    public string SourcePath { get; }

    /// <summary>The chat file's text.</summary>
    public string ChatText { get; }

    /// <summary>Names of every file shipped beside the chat text.</summary>
    public IReadOnlyCollection<string> FileNames => _zip is not null ? _zipEntries.Keys : _folderFiles.Keys;

    private WhatsAppExport(string sourcePath, string title, string chatText, ZipArchive? zip)
    {
        SourcePath = sourcePath;
        Title = title;
        ChatText = chatText;
        _zip = zip;
    }

    public static WhatsAppExport Open(string path)
    {
        if (Directory.Exists(path)) return OpenFolder(Path.GetFullPath(path));
        if (!File.Exists(path)) throw new FileNotFoundException("No export at this path.", path);
        var full = Path.GetFullPath(path);
        return Path.GetExtension(full).Equals(".zip", StringComparison.OrdinalIgnoreCase)
            ? OpenZip(full)
            : OpenText(full);
    }

    private static WhatsAppExport OpenZip(string path)
    {
        var zip = ZipFile.OpenRead(path);
        try
        {
            var files = zip.Entries.Where(e => e.Length > 0 && !e.FullName.EndsWith('/')).ToList();
            var chat = PickChatFile(files.Select(e => e.FullName).ToList())
                ?? throw new InvalidDataException("This zip holds no chat text file (_chat.txt or a WhatsApp .txt).");
            var chatEntry = files.First(e => e.FullName == chat);
            string text;
            using (var reader = new StreamReader(chatEntry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                text = reader.ReadToEnd();

            var export = new WhatsAppExport(path, TitleFor(chat, path), text, zip);
            foreach (var e in files)
            {
                if (e.FullName == chat) continue;
                export._zipEntries.TryAdd(e.Name, e);
            }
            return export;
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }

    private static WhatsAppExport OpenFolder(string path)
    {
        var files = Directory.GetFiles(path).ToList();
        var chat = PickChatFile(files.Select(Path.GetFileName).ToList()!)
            ?? throw new InvalidDataException("This folder holds no chat text file (_chat.txt or a WhatsApp .txt).");
        var chatPath = Path.Combine(path, chat);
        var export = new WhatsAppExport(path, TitleFor(chat, path), File.ReadAllText(chatPath, Encoding.UTF8), null);
        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            if (name.Equals(chat, StringComparison.OrdinalIgnoreCase)) continue;
            export._folderFiles.TryAdd(name, f);
        }
        return export;
    }

    private static WhatsAppExport OpenText(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        var export = new WhatsAppExport(path, TitleFor(Path.GetFileName(path), path), File.ReadAllText(path, Encoding.UTF8), null);
        // A bare .txt may still sit beside its media, as when a zip was unpacked.
        foreach (var f in Directory.GetFiles(folder))
        {
            var name = Path.GetFileName(f);
            if (f.Equals(path, StringComparison.OrdinalIgnoreCase)) continue;
            export._folderFiles.TryAdd(name, f);
        }
        return export;
    }

    /// <summary>The chat's text file among the export's files: _chat.txt, else the one WhatsApp .txt.</summary>
    internal static string? PickChatFile(IReadOnlyList<string> names)
    {
        var chat = names.FirstOrDefault(n => Path.GetFileName(n).Equals("_chat.txt", StringComparison.OrdinalIgnoreCase));
        if (chat is not null) return chat;
        var texts = names.Where(n => Path.GetExtension(n).Equals(".txt", StringComparison.OrdinalIgnoreCase)).ToList();
        return texts.FirstOrDefault(n => TitleFromName.IsMatch(Path.GetFileNameWithoutExtension(n)))
            ?? (texts.Count == 1 ? texts[0] : null);
    }

    /// <summary>The chat's name from the chat file's name, else from the zip or folder name.</summary>
    internal static string TitleFor(string chatFile, string sourcePath)
    {
        foreach (var candidate in new[] { Path.GetFileNameWithoutExtension(chatFile), Path.GetFileNameWithoutExtension(sourcePath) })
        {
            var m = TitleFromName.Match(candidate);
            if (m.Success) return m.Groups["title"].Value.Trim();
        }
        var source = Path.GetFileNameWithoutExtension(sourcePath.TrimEnd('\\', '/'));
        return source.Equals("_chat", StringComparison.OrdinalIgnoreCase) ? "Chat" : source;
    }

    /// <summary>The size of a shipped file, or null when the export does not carry it.</summary>
    public long? SizeOf(string name)
    {
        if (_zipEntries.TryGetValue(name, out var e)) return e.Length;
        if (_folderFiles.TryGetValue(name, out var f)) return new FileInfo(f).Length;
        return null;
    }

    /// <summary>Opens a shipped file for reading, or null when the export does not carry it.</summary>
    public Stream? OpenFile(string name)
    {
        if (_zipEntries.TryGetValue(name, out var e)) return e.Open();
        if (_folderFiles.TryGetValue(name, out var f)) return File.OpenRead(f);
        return null;
    }

    public IReadOnlyList<ParsedMessage> ReadMessages()
        => WhatsAppChatParser.Parse(ChatText, new HashSet<string>(FileNames, StringComparer.OrdinalIgnoreCase));

    public void Dispose() => _zip?.Dispose();
}
