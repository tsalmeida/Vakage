using System.Runtime.InteropServices;
using Vakage.Core.Library;

namespace Vakage;

/// <summary>
/// The headless side of Vakage, for scripts and for checking an import without
/// the window:
///
///   Vakage --import &lt;export.zip|folder|chat.txt&gt; --account &lt;name&gt; [--title &lt;chat name&gt;] [--me &lt;name&gt;]
///   Vakage --import-ios &lt;WhatsApp shared container&gt; --account &lt;name&gt; [--chat &lt;name or number&gt;[,…]] [--me &lt;name&gt;]
///   Vakage --join &lt;chat id&gt;,&lt;chat id&gt; --title &lt;name&gt;
///   Vakage --list [--account &lt;name&gt;]
///   Vakage --files --account &lt;name&gt; [--sender &lt;name&gt;] [--top &lt;n&gt;]
///
/// Every command takes --library &lt;folder&gt; to work on another library than
/// the one in settings.
/// </summary>
internal static class CommandLine
{
    public static bool Requested(string[] args)
        => args.Any(a => a is "--import" or "--import-ios" or "--join" or "--list" or "--files" or "--help" or "-h");

    public static int Run(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            if (options.Has("--help") || options.Has("-h"))
            {
                Console.WriteLine(Usage);
                return 0;
            }

            var settings = AppSettings.Load();
            var library = ArchiveLibrary.Open(options.Value("--library") ?? settings.ResolvedLibraryPath);

            if (options.Value("--import") is { } export)
                return Import(library, export, options);
            if (options.Value("--import-ios") is { } container)
                return ImportIos(library, container, options);
            if (options.Value("--join") is { } ids)
                return Join(library, ids, options);
            if (options.Has("--files"))
                return Files(library, options);
            return List(library, options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Vakage: {ex.Message}");
            AppLog.Error("Command line failed", ex);
            return 1;
        }
    }

    private const string Usage = """
        Vakage --import <export.zip|folder|chat.txt> --account <name> [--title <chat name>] [--me <name>]
        Vakage --import-ios <WhatsApp shared container from an iPhone backup> --account <name> [--chat <name or number>[,…]] [--me <name>]
        Vakage --join <chat id>,<chat id>[,…] --title <name>
        Vakage --list [--account <name>]
        Vakage --files --account <name> [--sender <name>] [--top <n>]
        Any command: --library <folder> (default: the library in settings)
        """;

    private static int Import(ArchiveLibrary library, string export, Options options)
    {
        var accountName = options.Value("--account")
            ?? throw new ArgumentException("--import needs --account <name>: whose phone the export came from.");
        var account = library.AddAccount(accountName);
        if (options.Value("--me") is { } me) library.SetSelfName(account.Id, me);

        var r = library.ImportWhatsApp(account.Id, export, options.Value("--title"));
        Console.WriteLine($"{(r.NewChat ? "New chat" : "Existing chat")} \"{r.Title}\" in {account.Name}: " +
                          $"{r.MessagesAdded} messages added, {r.MessagesAlreadyThere} already there; " +
                          $"{r.FilesStored} files stored, {r.FilesAlreadyStored} already in the library, {r.FilesMissing} not in the export.");
        return 0;
    }

    private static int ImportIos(ArchiveLibrary library, string container, Options options)
    {
        var accountName = options.Value("--account")
            ?? throw new ArgumentException("--import-ios needs --account <name>: whose phone the backup came from.");
        var account = library.AddAccount(accountName);
        if (options.Value("--me") is { } me) library.SetSelfName(account.Id, me);
        var filter = options.Value("--chat")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var results = library.ImportWhatsAppIos(account.Id, container, filter, line => Console.WriteLine($"Importing {line}…"));
        if (results.Count == 0) Console.WriteLine("No chat matched.");
        foreach (var r in results)
            Console.WriteLine($"  [{r.ChatId}] {r.Title}: {r.MessagesAdded} messages added, {r.MessagesAlreadyThere} already there; " +
                              $"{r.FilesStored} files stored, {r.FilesAlreadyStored} already in the library, {r.FilesMissing} missing from the backup.");
        return 0;
    }

    private static int Join(ArchiveLibrary library, string ids, Options options)
    {
        var chatIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(i => long.Parse(i, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var title = options.Value("--title") ?? throw new ArgumentException("--join needs --title <name>.");
        library.Join(chatIds, title);
        Console.WriteLine($"Joined {chatIds.Count} chats as {title}.");
        return 0;
    }

    private static int List(ArchiveLibrary library, Options options)
    {
        var only = options.Value("--account");
        foreach (var account in library.Accounts())
        {
            if (only is not null && !account.Name.Equals(only, StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"{account.Name} (you: {library.SelfNameFor(account.Id) ?? "unknown"})");
            foreach (var c in library.Conversations(account.Id))
                Console.WriteLine($"  [{string.Join(",", c.ChatIds)}] {c.Title}{(c.IsJoined ? $" [joined: {c.ChatIds.Count} chats]" : "")}  {c.MessageCount} messages, {c.First:yyyy-MM-dd} to {c.Last:yyyy-MM-dd}");
        }
        return 0;
    }

    private static int Files(ArchiveLibrary library, Options options)
    {
        var name = options.Value("--account") ?? throw new ArgumentException("--files needs --account <name>.");
        var account = library.Accounts().FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No account named {name}.");
        var top = int.TryParse(options.Value("--top"), out var n) ? n : 50;
        var files = library.Files(account.Id, options.Value("--sender"));
        Console.WriteLine($"{files.Count} files, {Format.Bytes(files.Sum(f => f.Size))}");
        foreach (var f in files.Take(top))
            Console.WriteLine($"  {Format.Bytes(f.Size),10}  {f.Name}  from {f.Senders} in {f.Chats}, {f.FirstSent:yyyy-MM-dd}");
        return 0;
    }

    private sealed class Options
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (var i = 0; i < args.Length; i++)
            {
                var name = args[i];
                if (!name.StartsWith('-')) continue;
                var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;
                o._values[name] = value;
            }
            return o;
        }

        public bool Has(string name) => _values.ContainsKey(name);
        public string? Value(string name) => _values.GetValueOrDefault(name);
    }
}

internal static class Format
{
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.0} {units[unit]}";
    }
}

internal static class ParentConsole
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;

    public static void Attach()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (GetStdHandle(StdOutputHandle) != IntPtr.Zero) return;
        AttachConsole(AttachParentProcess);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);
}
