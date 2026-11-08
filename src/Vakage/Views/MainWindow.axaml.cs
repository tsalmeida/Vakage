using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Vakage.Core.Library;
using Vakage.Core.Sources.WhatsApp;
using Vakage.Design;

namespace Vakage.Views;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };

    private readonly AppSettings _settings = AppSettings.Load();
    private ArchiveLibrary _library = null!;
    private Account? _account;
    private string? _selfName;
    private bool _loading;

    public MainWindow()
    {
        InitializeComponent();

        ConversationList.ItemTemplate = new FuncDataTemplate<Conversation>((c, _) => ConversationRow(c));
        MessageList.ItemTemplate = new FuncDataTemplate<object>((row, _) => row switch
        {
            DayRow d => DayLine(d),
            StoredMessage m => Bubble(m),
            _ => new TextBlock(),
        });
        FileList.ItemTemplate = new FuncDataTemplate<StoredFile>((f, _) => FileRow(f));
        SenderModeBox.ItemsSource = new[] { "Sent by", "Not sent by" };
        SenderModeBox.SelectedIndex = 0;
        TypeBox.ItemsSource = FileTypes.Select(t => t.Label).ToList();
        TypeBox.SelectedIndex = 0;
        MessageList.ContextMenu = MessageMenu();

        try
        {
            _library = ArchiveLibrary.Open(_settings.ResolvedLibraryPath);
            ReloadAccounts(_settings.LastAccount);
            Status($"Library: {_library.Root}");
        }
        catch (Exception ex)
        {
            AppLog.Error("The library could not be opened", ex);
            Status($"The library at {_settings.ResolvedLibraryPath} could not be opened: {ex.Message}");
        }
    }

    // ----- Accounts -----

    private void ReloadAccounts(string? select)
    {
        _loading = true;
        var accounts = _library.Accounts();
        AccountBox.ItemsSource = accounts.Select(a => a.Name).ToList();
        _loading = false;
        var index = accounts.ToList().FindIndex(a => a.Name.Equals(select, StringComparison.OrdinalIgnoreCase));
        AccountBox.SelectedIndex = accounts.Count == 0 ? -1 : Math.Max(index, 0);
        if (accounts.Count == 0) ShowAccount(null);
    }

    private void OnAccountChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var name = AccountBox.SelectedItem as string;
        ShowAccount(name is null ? null : _library.Accounts().FirstOrDefault(a => a.Name == name));
    }

    private void ShowAccount(Account? account)
    {
        _account = account;
        ImportFileButton.IsEnabled = ImportFolderButton.IsEnabled = account is not null;
        if (account is not null)
        {
            _settings.LastAccount = account.Name;
            _settings.Save();
        }
        ReloadSelf();
        ReloadConversations();
        ReloadFiles();
    }

    private async void OnNewAccountClick(object? sender, RoutedEventArgs e)
    {
        var name = await Dialogs.AskText(this, "New account",
            "Whose phone do the exports come from? One account per person (and per phone number, if you want them apart).");
        if (string.IsNullOrWhiteSpace(name)) return;
        var account = _library.AddAccount(name);
        ReloadAccounts(account.Name);
        Status($"Account {account.Name} is ready. Import its exported chats with Import export.");
    }

    /// <summary>
    /// The owner's name in exports, which carry no "from me" mark: worked out
    /// from the chats (the sender found in all of them). A phone's database
    /// marks the owner's messages itself.
    /// </summary>
    private void ReloadSelf() => _selfName = _account is null ? null : _library.SelfNameFor(_account.Id);

    // ----- Import -----

    private async void OnImportFileClick(object? sender, RoutedEventArgs e)
    {
        if (_account is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Import exported chats into {_account.Name}",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("WhatsApp export") { Patterns = ["*.zip", "*.txt"] },
            ],
        });
        await Import(files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList());
    }

    private async void OnImportFolderClick(object? sender, RoutedEventArgs e)
    {
        if (_account is null) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Import an unzipped export into {_account.Name}",
            AllowMultiple = true,
        });
        await Import(folders.Select(f => f.TryGetLocalPath()).OfType<string>().ToList());
    }

    private async Task Import(IReadOnlyList<string> paths)
    {
        if (_account is null || paths.Count == 0) return;
        var account = _account;
        ImportFileButton.IsEnabled = ImportFolderButton.IsEnabled = false;
        var lines = new List<string>();
        foreach (var path in paths)
        {
            if (WhatsAppIosDatabase.IsDatabase(path))
            {
                await ImportPhoneDatabase(account, path, lines);
                continue;
            }
            Status($"Importing {Path.GetFileName(path)} into {account.Name}…");
            try
            {
                var r = await Task.Run(() => _library.ImportWhatsApp(account.Id, path));
                lines.Add($"{r.Title}: {r.MessagesAdded} messages added" +
                          (r.MessagesAlreadyThere > 0 ? $", {r.MessagesAlreadyThere} already there" : "") +
                          $", {r.FilesStored} files stored" +
                          (r.FilesAlreadyStored > 0 ? $", {r.FilesAlreadyStored} already in the library" : "") +
                          (r.FilesMissing > 0 ? $", {r.FilesMissing} not in the export" : "") + ".");
            }
            catch (Exception ex)
            {
                AppLog.Error($"Import of {path} failed", ex);
                lines.Add($"{Path.GetFileName(path)} was not imported: {ex.Message}");
            }
        }
        Status("Working out which files look important and which irrelevant…");
        var suggested = await Task.Run(() => _library.Suggest());
        lines.Add($"Suggestions: {suggested.Important:N0} important, {suggested.Irrelevant:N0} irrelevant.");
        ImportFileButton.IsEnabled = ImportFolderButton.IsEnabled = true;
        ReloadSelf();
        ReloadConversations();
        ReloadFiles();
        Status(string.Join("  ", lines));
    }

    /// <summary>
    /// A folder holding an iPhone's WhatsApp database (ChatStorage.sqlite, from
    /// a decrypted backup) brings in every chat on that phone, after asking.
    /// </summary>
    private async Task ImportPhoneDatabase(Account account, string path, List<string> lines)
    {
        try
        {
            var chats = WhatsAppIosDatabase.Open(path).Chats();
            var ok = await Dialogs.Confirm(this, "Import a phone's WhatsApp",
                $"This folder holds the WhatsApp database of a phone: {chats.Count:N0} chats, {chats.Sum(c => c.MessageCount):N0} messages. " +
                $"Import all of them into {account.Name}'s archive? Chats already there take only what is new; files are copied, and the folder is left as it is. " +
                "To bring in only some chats, use Vakage --import-ios with --chat.",
                "Import all", destructive: false);
            if (!ok) return;
            var results = await Task.Run(() => _library.ImportWhatsAppIos(account.Id, path, null, line => Status($"Importing {line}…")));
            lines.Add($"{results.Count} chats from the phone: {results.Sum(r => r.MessagesAdded):N0} messages added, " +
                      $"{results.Sum(r => r.FilesStored):N0} files stored, {results.Sum(r => r.FilesMissing):N0} not in the backup.");
        }
        catch (Exception ex)
        {
            AppLog.Error($"Import of the phone database at {path} failed", ex);
            lines.Add($"The phone's database was not imported: {ex.Message}");
        }
    }

    // ----- Conversations -----

    private void ReloadConversations(bool keepSelection = false)
    {
        var keep = keepSelection ? _shown?.ChatIds : null;
        var conversations = _account is null ? [] : _library.Conversations(_account.Id);
        ConversationList.ItemsSource = conversations;
        ConversationsCaption.Text = _account is null
            ? "Start with a new account (the person button beside Account), one per person whose phone the exports come from. Then import its exported chats."
            : conversations.Count == 0
                ? $"No chats in {_account.Name} yet. Export a chat on the phone (Export chat, Include media) and import the zip."
                : $"{conversations.Count} in {_account.Name}. Ctrl+click to select several: the same person, or to delete.";
        var again = keep is null ? -1 : conversations.ToList().FindIndex(c => c.ChatIds.SequenceEqual(keep));
        if (again >= 0)
        {
            ConversationList.SelectionChanged -= OnConversationSelectionChanged;
            ConversationList.SelectedIndex = again;
            ConversationList.SelectionChanged += OnConversationSelectionChanged;
        }
        else if (conversations.Count > 0) ConversationList.SelectedIndex = 0;
        else ShowSelectedConversation();
        ReloadWith(conversations);
    }

    private void OnConversationSelectionChanged(object? sender, SelectionChangedEventArgs e) => ShowSelectedConversation();

    private List<Conversation> SelectedConversations()
        => ConversationList.SelectedItems?.OfType<Conversation>().ToList() ?? [];

    /// <summary>One chat history of a person shown alone; null shows all of them together.</summary>
    private long? _history;
    private Conversation? _shown;

    private List<object> _rows = [];

    private void ShowSelectedConversation() => ShowSelectedConversation(null);

    /// <param name="scrollTo">The row to show; the latest message when null.</param>
    private void ShowSelectedConversation(int? scrollTo)
    {
        var selected = SelectedConversations();
        JoinButton.IsVisible = selected.Count >= 2;
        CalendarButton.IsEnabled = MediaButton.IsEnabled = SettingsButton.IsEnabled = selected.Count == 1;
        SelectionBar.IsVisible = false;
        HistoryBar.Children.Clear();

        if (selected.Count != 1)
        {
            ChatTitle.Text = selected.Count == 0 ? "" : $"{selected.Count} conversations selected";
            ChatCaption.Text = selected.Count == 0 ? "" : "Same person ties them together: each history stays apart, readable alone or all together.";
            MessageList.ItemsSource = null;
            _shown = null;
            return;
        }

        var c = selected[0];
        if (_shown?.ChatIds.SequenceEqual(c.ChatIds) != true) _history = null;
        _shown = c;
        var chats = _library.Chats(_account!.Id).Where(x => c.ChatIds.Contains(x.Id)).ToList();
        var noisy = chats.Count > 0 && chats.All(x => x.Noisy);

        var showing = _history is { } one ? [one] : c.ChatIds;
        var messages = _library.Messages(showing);
        ChatTitle.Text = c.Title;
        ChatCaption.Text = $"{_account?.Name}'s archive · {c.MessageCount:N0} messages · {c.First:d MMM yyyy} to {c.Last:d MMM yyyy}" +
                           (c.IsJoined ? $" · the same person in {c.ChatIds.Count} chat histories" : "") +
                           (noisy ? " · noisy: its pictures are pruned hard" : "");
        _chatTitles = c.IsJoined && _history is null ? chats.ToDictionary(x => x.Id, x => x.Title) : null;

        if (c.IsJoined)
        {
            HistoryBar.Children.Add(HistoryChip("All together", $"{c.MessageCount:N0}", null));
            foreach (var chat in chats.OrderBy(x => x.First))
            {
                // The person's name is the title already; a history is told by its number.
                var label = chat.Title.StartsWith(c.Title + " (", StringComparison.Ordinal) ? chat.Title[(c.Title.Length + 2)..].TrimEnd(')') : chat.Title;
                HistoryChip(label, $"{chat.MessageCount:N0} messages, {chat.First:yyyy}–{chat.Last:yyyy}", chat.Id);
            }
        }

        var rows = new List<object>(messages.Count + 64);
        DateTime? day = null;
        foreach (var m in messages)
        {
            if (day != m.SentAt.Date)
            {
                day = m.SentAt.Date;
                rows.Add(new DayRow(m.SentAt.Date));
            }
            rows.Add(m);
        }
        _rows = rows;
        MessageList.ItemsSource = rows;
        // Opens on the latest message, as a chat does; after layout, or it lands short.
        if (rows.Count > 0)
        {
            var target = Math.Clamp(scrollTo ?? rows.Count - 1, 0, rows.Count - 1);
            Dispatcher.UIThread.Post(() => MessageList.ScrollIntoView(target), DispatcherPriority.Background);
        }
    }

    // ----- Calendar, media, settings -----

    private async void OnCalendarClick(object? sender, RoutedEventArgs e)
    {
        if (_shown is not { } c) return;
        var days = _rows.OfType<StoredMessage>().GroupBy(m => m.SentAt.Date).ToDictionary(g => g.Key, g => g.Count());
        var title = _history is not null && _chatTitles is null ? $"{c.Title} ({ChatTitle.Text})" : c.Title;
        if (await CalendarView.Pick(this, c.Title, days) is { } day)
        {
            var index = _rows.FindIndex(r => r is DayRow d && d.Day == day);
            if (index >= 0)
            {
                MessageList.ScrollIntoView(Math.Min(index + 8, _rows.Count - 1));
                Dispatcher.UIThread.Post(() => MessageList.ScrollIntoView(index), DispatcherPriority.Background);
                Status($"{c.Title}, {day:dddd d MMMM yyyy}: {days[day]:N0} messages.");
            }
        }
    }

    private void OnMediaClick(object? sender, RoutedEventArgs e)
    {
        if (_shown is not { } c) return;
        _loading = true;
        WithBox.SelectedItem = (WithBox.ItemsSource as IEnumerable<WithChoice>)?.FirstOrDefault(w => w.ChatIds.SequenceEqual(c.ChatIds));
        _loading = false;
        ShowTab(files: true);
        ShowFiles();
    }

    private async void OnChatSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_shown is not { } c || _account is null) return;
        var chats = _library.Chats(_account.Id).Where(x => c.ChatIds.Contains(x.Id)).ToList();
        switch (await ChatSettings.Show(this, c, chats))
        {
            case ChatSettingsChoice.ToggleNoisy:
                var make = !(chats.Count > 0 && chats.All(x => x.Noisy));
                _library.SetNoisy(c.ChatIds, make);
                Status(make ? $"{c.Title} is noisy: working out again which of its files to keep…" : $"{c.Title} is no longer noisy.");
                var r = await Task.Run(() => _library.Suggest());
                ShowSelectedConversation();
                ReloadFiles();
                Status($"{c.Title} {(make ? "is noisy" : "is no longer noisy")}. Suggestions now: {r.Important:N0} important, {r.Irrelevant:N0} irrelevant.");
                break;
            case ChatSettingsChoice.Separate when c.ThreadId is { } thread:
                _library.Unjoin(thread);
                ReloadConversations();
                Status($"{c.Title}'s {c.ChatIds.Count} chats are listed apart again.");
                break;
            case ChatSettingsChoice.Delete:
                var ok = await Dialogs.Confirm(this, "Delete conversation",
                    $"Delete {c.Title} from {_account.Name}'s archive? Its messages go, and every file no other conversation uses (files marked important stay). Later imports leave it out. This cannot be undone.",
                    "Delete");
                if (!ok) return;
                var freed = _library.DeleteChats(c.ChatIds);
                ReloadSelf();
                ReloadConversations();
                ReloadFiles();
                Status($"Deleted {c.Title}; {Format.Bytes(freed)} freed.");
                break;
        }
    }

    // ----- Picking messages -----

    private List<StoredMessage> SelectedMessages() => MessageList.SelectedItems?.OfType<StoredMessage>().ToList() ?? [];

    private void OnMessageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var picked = SelectedMessages();
        SelectionBar.IsVisible = picked.Count > 0;
        var withMedia = picked.Count(m => m.FileId is not null);
        SelectionCaption.Text = $"{picked.Count:N0} message(s) picked, {withMedia:N0} with a file" +
                                (withMedia > 0 ? $" ({Format.Bytes(picked.Where(m => m.FileId is not null && m.Reduced is null).Sum(m => m.FileSize ?? 0))})" : "");
    }

    private void OnClearSelectionClick(object? sender, RoutedEventArgs e) => MessageList.SelectedItems?.Clear();

    private int FirstPickedRow() => SelectedMessages().Select(m => _rows.IndexOf(m)).Where(i => i >= 0).DefaultIfEmpty(_rows.Count - 1).Min();

    private void OnMessagesMarkClick(object? sender, RoutedEventArgs e)
    {
        var mark = (sender as Control)?.Tag as string;
        var files = _library.FilesOf(SelectedMessages().Select(m => m.Id).ToList());
        if (files.Count == 0) { Status("None of the picked messages has a file."); return; }
        _library.Mark(files, mark);
        MessageList.SelectedItems?.Clear();
        ReloadFiles();
        Status($"{files.Count:N0} file(s) marked {mark}.");
    }

    private async void OnMessagesCompressClick(object? sender, RoutedEventArgs e)
    {
        if (_account is null) return;
        var account = _account;
        var picked = SelectedMessages();
        var files = _library.FilesOf(picked.Select(m => m.Id).ToList());
        if (files.Count == 0) { Status("None of the picked messages has a file."); return; }
        var row = FirstPickedRow();
        var reader = new EidokanonTextReader();
        Status($"Compressing {files.Count:N0} file(s)…");
        var r = await Task.Run(() => _library.Reduce(account.Id, files, reader, line => Status($"Compressing {line}"), chosen: true));
        ShowSelectedConversation(row);
        ReloadFiles();
        Status($"Compressed: {r.ToText:N0} kept as text, {r.ToThumbnail:N0} as a thumbnail, {r.Removed:N0} removed with a notice, {r.Kept:N0} left whole " +
               $"(important, or nothing safe to keep in their place); {Format.Bytes(r.BytesFreed)} freed.");
    }

    private async void OnMessagesDeleteMediaClick(object? sender, RoutedEventArgs e)
    {
        var picked = SelectedMessages().Where(m => m.FileId is not null || m.MessageThumb is not null).ToList();
        if (picked.Count == 0) { Status("None of the picked messages has a file."); return; }
        if (!await Dialogs.Confirm(this, "Delete media",
                $"Delete the files of {picked.Count:N0} message(s)? The messages stay, with a notice where each file was. A file another message still uses stays for that one, and files marked important are kept. This cannot be undone.",
                "Delete media")) return;
        var row = FirstPickedRow();
        var freed = _library.DeleteMedia(picked.Select(m => m.Id).ToList());
        ShowSelectedConversation(row);
        ReloadFiles();
        Status($"Deleted the media of {picked.Count:N0} message(s); {Format.Bytes(freed)} freed.");
    }

    private async void OnMessagesDeleteClick(object? sender, RoutedEventArgs e)
    {
        var picked = SelectedMessages();
        if (picked.Count == 0) return;
        if (!await Dialogs.Confirm(this, "Delete messages",
                $"Delete {picked.Count:N0} message(s) completely, with their files (a file another message uses, or one marked important, stays)? Later imports leave them out. This cannot be undone.",
                "Delete")) return;
        var row = FirstPickedRow();
        var freed = _library.DeleteMessages(picked.Select(m => m.Id).ToList());
        ShowSelectedConversation(row);
        ReloadConversations(keepSelection: true);
        ReloadFiles();
        Status($"Deleted {picked.Count:N0} message(s); {Format.Bytes(freed)} freed.");
    }

    /// <summary>The same actions on a right click, for whatever is picked.</summary>
    private ContextMenu MessageMenu()
    {
        MenuItem Item(string text, string glyph, string hue, EventHandler<RoutedEventArgs> act, string? tag = null)
        {
            var item = new MenuItem { Header = text, Tag = tag, Icon = new TextBlock { Text = ActionIcons.Glyph(glyph), FontFamily = ActionIcons.Solid, Foreground = Palette.ByName(hue)!.Brush500 } };
            item.Click += act;
            return item;
        }
        return new ContextMenu
        {
            ItemsSource = new object[]
            {
                Item("Mark media important", ActionIcons.Star, "lime", OnMessagesMarkClick, "important"),
                Item("Mark media irrelevant", ActionIcons.Ban, "orange", OnMessagesMarkClick, "irrelevant"),
                new Separator(),
                Item("Compress media", ActionIcons.Compress, "orange", OnMessagesCompressClick),
                Item("Delete media", ActionIcons.Eraser, "orange", OnMessagesDeleteMediaClick),
                Item("Delete messages", ActionIcons.TrashCan, "orange", OnMessagesDeleteClick),
            },
        };
    }

    /// <summary>A button for one of a person's chat histories, or for all of them together.</summary>
    private Button HistoryChip(string title, string count, long? chatId)
    {
        var chip = new Button { Margin = new Thickness(0, 4, 8, 4) };
        chip.Classes.Add("pill");
        chip.Classes.Add("blue");
        chip.Classes.Set("active", _history == chatId);
        chip.Content = ActionIcons.Label(chatId is null ? ActionIcons.Link : ActionIcons.Comments, $"{title} · {count}", "blue");
        ToolTip.SetTip(chip, chatId is null ? "Every history of this person, as one stream" : "This history alone");
        chip.Click += (_, _) =>
        {
            _history = chatId;
            ShowSelectedConversation();
        };
        if (chatId is not null) HistoryBar.Children.Add(chip);
        return chip;
    }

    private Dictionary<long, string>? _chatTitles;

    private async void OnJoinClick(object? sender, RoutedEventArgs e)
    {
        var selected = SelectedConversations();
        if (selected.Count < 2) return;
        var title = await Dialogs.AskText(this, "The same person",
            "These chats are one person, under different numbers or accounts. Each history stays as it was, readable alone or all together; Separate undoes it. The person's name:",
            selected[0].Title.Contains(" (+") ? selected[0].Title[..selected[0].Title.IndexOf(" (+", StringComparison.Ordinal)] : selected[0].Title);
        if (string.IsNullOrWhiteSpace(title)) return;
        try
        {
            _library.Join(selected.SelectMany(c => c.ChatIds).Distinct().ToList(), title);
            ReloadConversations();
            Status($"{title}: {selected.Count} chat histories of the same person.");
        }
        catch (Exception ex)
        {
            Status(ex.Message);
        }
    }

    // ----- Files -----

    private void ReloadFiles()
    {
        _loading = true;
        var senders = _account is null ? [] : _library.Senders(_account.Id).ToList();
        var previous = SenderBox.SelectedItem as string;
        SenderBox.ItemsSource = new[] { "Anyone" }.Concat(senders).ToList();
        SenderBox.SelectedItem = previous is not null && senders.Contains(previous) ? previous : "Anyone";
        _loading = false;
        ShowFiles();
    }

    private void OnFilesFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_loading) ShowFiles();
    }

    private FileList _fileList = Core.Library.FileList.All;

    private sealed record WithChoice(string Label, IReadOnlyList<long> ChatIds)
    {
        public override string ToString() => Label;
    }

    private static readonly (string Label, string[] Types)[] FileTypes =
    [
        ("All kinds", []),
        ("Pictures", ["image"]),
        ("Videos", ["video"]),
        ("Audio", ["audio"]),
        ("Documents", ["document"]),
        ("Stickers and GIFs", ["sticker", "gif"]),
    ];

    private void ReloadWith(IReadOnlyList<Conversation> conversations)
    {
        _loading = true;
        var previous = (WithBox.SelectedItem as WithChoice)?.ChatIds;
        var choices = new List<WithChoice> { new("With anyone", []) };
        choices.AddRange(conversations.OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase).Select(c => new WithChoice(c.Title, c.ChatIds)));
        WithBox.ItemsSource = choices;
        WithBox.SelectedItem = previous is null ? choices[0] : choices.FirstOrDefault(c => c.ChatIds.SequenceEqual(previous)) ?? choices[0];
        _loading = false;
    }

    private void OnFileListClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<FileList>(tag, out var list)) return;
        _fileList = list;
        ImportantTab.Classes.Set("active", list == Core.Library.FileList.Important);
        IrrelevantTab.Classes.Set("active", list == Core.Library.FileList.Irrelevant);
        UnsortedTab.Classes.Set("active", list == Core.Library.FileList.Unsorted);
        DocumentsTab.Classes.Set("active", list == Core.Library.FileList.Documents);
        AllFilesTab.Classes.Set("active", list == Core.Library.FileList.All);
        ReduceButton.IsVisible = list == Core.Library.FileList.Irrelevant;
        ShowFiles();
    }

    private void ShowFiles()
    {
        if (_account is null)
        {
            FileList.ItemsSource = null;
            FilesCaption.Text = "";
            return;
        }
        var who = SenderBox.SelectedItem as string;
        var person = who is null or "Anyone" ? null : who;
        var exclude = SenderModeBox.SelectedIndex == 1;
        var with = WithBox.SelectedItem as WithChoice;
        var types = FileTypes[Math.Max(0, TypeBox.SelectedIndex)].Types;
        var files = _library.Files(_account.Id, person, exclude, _fileList, with?.ChatIds, types);
        FileList.ItemsSource = files;
        var whole = files.Where(f => f.Reduced is null).ToList();
        var list = _fileList switch
        {
            Core.Library.FileList.Important => "important ",
            Core.Library.FileList.Irrelevant => "irrelevant ",
            Core.Library.FileList.Unsorted => "unsorted ",
            Core.Library.FileList.Documents => "document ",
            _ => "",
        };
        var kind = FileTypes[Math.Max(0, TypeBox.SelectedIndex)].Label.ToLowerInvariant();
        FilesCaption.Text = $"{files.Count:N0} {list}files" + (types.Length > 0 ? $" ({kind})" : "") + (with is { ChatIds.Count: > 0 } ? $" with {with.Label}" : $" in {_account.Name}'s archive") +
                            $", {Format.Bytes(whole.Sum(f => f.Size))} kept whole" +
                            (files.Count > whole.Count ? $", {files.Count - whole.Count:N0} already reduced" : "") +
                            ". A question mark is Vakage's suggestion; your mark always wins. Ctrl+click or Shift+click to select several.";
        OnFileSelectionChanged(null, null!);
    }

    private List<StoredFile> SelectedFiles() => FileList.SelectedItems?.OfType<StoredFile>().ToList() ?? [];

    private void OnFileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = SelectedFiles();
        MarkImportantButton.IsEnabled = MarkIrrelevantButton.IsEnabled = ClearMarkButton.IsEnabled = selected.Count > 0;
        OpenFileButton.IsEnabled = selected.Count == 1 && selected[0].Reduced is null;
    }

    private void OnMarkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        var selected = SelectedFiles();
        if (selected.Count == 0) return;
        var mark = tag.Length == 0 ? null : tag;
        _library.Mark(selected.Select(f => f.Id).ToList(), mark);
        ShowFiles();
        Status(mark is null
            ? $"Your mark is off {selected.Count:N0} file(s); they follow Vakage's suggestion again."
            : $"{selected.Count:N0} file(s) marked {mark}.");
    }

    private async void OnReduceClick(object? sender, RoutedEventArgs e)
    {
        if (_account is null) return;
        var selected = SelectedFiles().Where(f => f.Reduced is null).ToList();
        var targets = selected.Count > 0 ? selected : (FileList.ItemsSource as IEnumerable<StoredFile>)?.Where(f => f.Reduced is null).ToList() ?? [];
        if (targets.Count == 0) return;
        var reader = new EidokanonTextReader();
        var ok = await Dialogs.Confirm(this, "Reduce irrelevant files",
            $"Let {targets.Count:N0} irrelevant file(s) go ({Format.Bytes(targets.Sum(f => f.Size))})? " +
            "Of each one Vakage keeps the text, when it is a picture of words or a PDF, or a small thumbnail; the original is deleted from the archive, " +
            "and where it was in the chat a notice says so. Files marked important are never touched. This cannot be undone." +
            (reader.Unavailable is { } why ? $" ({why} Pictures will keep a thumbnail.)" : ""),
            "Reduce");
        if (!ok) return;
        ReduceButton.IsEnabled = false;
        var ids = targets.Select(f => f.Id).ToList();
        var account = _account;
        var result = await Task.Run(() => _library.Reduce(account.Id, ids, reader, line => Status($"Reducing {line}")));
        ReduceButton.IsEnabled = true;
        ShowFiles();
        Status($"Reduced: {result.ToText:N0} kept as text, {result.ToThumbnail:N0} as thumbnails, {result.Removed:N0} removed with a notice, " +
               $"{result.Kept:N0} kept whole; {Format.Bytes(result.BytesFreed)} freed." + (result.Notes.Count > 0 ? "  " + result.Notes[0] : ""));
    }

    private void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedFiles() is [{ Reduced: null } f]) OpenPath(f.Path);
    }

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FileList.SelectedItem is StoredFile { Reduced: null } f) OpenPath(f.Path);
    }

    // ----- Tabs and folders -----

    private void OnConversationsTabClick(object? sender, RoutedEventArgs e) => ShowTab(files: false);

    private void OnFilesTabClick(object? sender, RoutedEventArgs e) => ShowTab(files: true);

    private void ShowTab(bool files)
    {
        ConversationsPanel.IsVisible = !files;
        FilesPanel.IsVisible = files;
        ConversationsTab.Classes.Set("active", !files);
        FilesTab.Classes.Set("active", files);
    }

    private void OnLibraryFolderClick(object? sender, RoutedEventArgs e) => OpenPath(_library.Root);

    private void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status($"Could not open {path}: {ex.Message}");
        }
    }

    private void Status(string text) => Dispatcher.UIThread.Post(() => StatusLine.Text = text);

    // ----- Rows -----

    private sealed record DayRow(DateTime Day);

    private static Control ConversationRow(Conversation c)
    {
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(4, 4) };
        var title = new TextBlock { Text = c.Title, TextTrimming = TextTrimming.CharacterEllipsis };
        if (c.IsJoined)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = ActionIcons.Glyph(ActionIcons.Link),
                FontFamily = ActionIcons.Solid,
                Foreground = Palette.Blue.Brush500,
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(title);
            panel.Children.Add(row);
        }
        else
        {
            panel.Children.Add(title);
        }
        var caption = new TextBlock
        {
            Text = $"{c.MessageCount:N0} messages · {c.First:MMM yyyy} to {c.Last:MMM yyyy}",
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        caption.Classes.Add("caption");
        panel.Children.Add(caption);
        return panel;
    }

    private static Control DayLine(DayRow d)
    {
        var t = new TextBlock
        {
            Text = d.Day.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 4),
        };
        t.Classes.Add("caption");
        return t;
    }

    private Control Bubble(StoredMessage m)
    {
        if (m.Kind == MessageKind.System)
        {
            var sys = new TextBlock
            {
                Text = $"{m.SentAt:HH:mm}  {m.Body}",
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                MaxWidth = Typography.ColumnWidth(Typography.Condensed, Typography.Caption) + 24,
                Margin = new Thickness(0, 2),
            };
            sys.Classes.Add("caption");
            return sys;
        }

        var mine = m.FromMe || (m.SenderKey is null && _selfName is not null && m.Sender == _selfName);
        var content = new StackPanel { Spacing = 4 };

        var meta = new TextBlock
        {
            Text = (m.Starred ? "★ " : "") + $"{m.Sender} · {m.SentAt:HH:mm}" +
                   (m.Forwarded ? m.ForwardScore >= 5 ? " · forwarded many times" : " · forwarded" : "") +
                   (_chatTitles is not null && _chatTitles.TryGetValue(m.ChatId, out var from) ? $" · {from}" : ""),
            TextWrapping = TextWrapping.Wrap,
        };
        meta.Classes.Add("caption");
        content.Children.Add(meta);

        if (m.QuotedSender is not null || m.QuotedBody is not null)
        {
            var quote = new TextBlock
            {
                Text = $"{m.QuotedSender}: {Shorten(m.QuotedBody ?? "", 160)}",
                TextWrapping = TextWrapping.Wrap,
                FontStyle = FontStyle.Italic,
            };
            quote.Classes.Add("caption");
            content.Children.Add(new Border
            {
                Child = quote,
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = Palette.Blue.Brush500,
                Padding = new Thickness(8, 2),
            });
        }

        if (m.Kind == MessageKind.MediaOmitted)
        {
            var omitted = new TextBlock { Text = "A file that never reached this archive (the phone had not downloaded it)." + (m.MessageThumb is not null ? " Its preview is kept:" : ""), FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap };
            omitted.Classes.Add("caption");
            content.Children.Add(omitted);
            if (m.MessageThumb is { } preview && File.Exists(preview))
            {
                try { content.Children.Add(new Image { Source = new Bitmap(preview), MaxWidth = 240, MaxHeight = 240, HorizontalAlignment = HorizontalAlignment.Left }); }
                catch (Exception) { /* not a picture */ }
            }
        }
        else if (m.MediaDeleted)
        {
            var gone = new TextBlock { Text = $"The media of this message ({m.AttachmentName}) was deleted.", FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap };
            gone.Classes.Add("caption");
            content.Children.Add(gone);
        }
        else if (m.Reduced is { } reduced && m.AttachmentName is { } let)
        {
            content.Children.Add(ReducedNotice(let, reduced, m.ThumbPath, m.FileText));
        }
        else if (m.AttachmentName is { } attachment)
        {
            content.Children.Add(Attachment(attachment, m.FilePath, m.FileSize, m.Duration));
        }

        if (m.Body.Length > 0)
        {
            var body = new SelectableTextBlock { Text = m.Body, TextWrapping = TextWrapping.Wrap };
            body.Classes.Add("prose");
            content.Children.Add(body);
        }
        if (m.LinkTitle is { } link)
        {
            var preview = new TextBlock { Text = $"🔗 {link}", TextWrapping = TextWrapping.Wrap };
            preview.Classes.Add("caption");
            if (m.LinkUrl is { } url) ToolTip.SetTip(preview, url);
            content.Children.Add(preview);
        }
        if (m.VCard is { } card && card.Split('\n').FirstOrDefault(l => l.StartsWith("FN:", StringComparison.OrdinalIgnoreCase)) is { } fn)
        {
            var contact = new TextBlock { Text = $"Contact card: {fn[3..].Trim()}", TextWrapping = TextWrapping.Wrap };
            contact.Classes.Add("caption");
            ToolTip.SetTip(contact, card);
            content.Children.Add(contact);
        }

        var bubble = new Border
        {
            Child = content,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(12, 10),
            Margin = new Thickness(mine ? 96 : 0, 3, mine ? 0 : 96, 3),
            MaxWidth = Typography.ColumnWidth(Typography.Sans, Typography.Prose) + 24,
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        };
        bubble[!Border.BackgroundProperty] = bubble.GetResourceObservable(mine ? "TkxUserBubble" : "TkxAssistantBubble").ToBinding();
        bubble[!Border.BorderBrushProperty] = bubble.GetResourceObservable(mine ? "TkxUserBubbleBorder" : "TkxAssistantBubbleBorder").ToBinding();
        return bubble;
    }

    /// <summary>Where an irrelevant file was let go: the notice, and the thumbnail or the words kept.</summary>
    private static Control ReducedNotice(string name, string reduced, string? thumb, string? text)
    {
        var panel = new StackPanel { Spacing = 4 };
        var notice = new TextBlock
        {
            Text = reduced switch
            {
                "text" => $"The original {name} was deleted as irrelevant; the text it contained is kept:",
                "thumbnail" => $"The original {name} was deleted as irrelevant; this thumbnail is kept.",
                _ => $"The original {name} was deleted as irrelevant.",
            },
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap,
        };
        notice.Classes.Add("caption");
        panel.Children.Add(notice);
        if (thumb is not null && File.Exists(thumb))
        {
            try
            {
                panel.Children.Add(new Image { Source = new Bitmap(thumb), MaxWidth = 240, MaxHeight = 240, HorizontalAlignment = HorizontalAlignment.Left });
            }
            catch (Exception ex)
            {
                AppLog.Error($"Could not show {thumb}", ex);
            }
        }
        if (reduced == "text" && !string.IsNullOrWhiteSpace(text))
        {
            var words = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            words.Classes.Add("prose");
            panel.Children.Add(words);
        }
        return panel;
    }

    private static string Shorten(string text, int length)
        => text.Length <= length ? text.Replace('\n', ' ') : text[..length].Replace('\n', ' ') + "…";

    private Control Attachment(string name, string? path, long? size, double? duration = null)
    {
        if (path is null)
        {
            var missing = new TextBlock { Text = $"{name} (not in the export)", TextWrapping = TextWrapping.Wrap };
            missing.Classes.Add("caption");
            return missing;
        }

        var panel = new StackPanel { Spacing = 4 };
        if (ImageExtensions.Contains(Path.GetExtension(path)))
        {
            var image = new Image { MaxHeight = 320, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform };
            // Decoded only once the row is on screen; the list is virtualised.
            image.AttachedToVisualTree += (_, _) =>
            {
                if (image.Source is not null) return;
                try
                {
                    using var stream = File.OpenRead(path);
                    image.Source = Bitmap.DecodeToWidth(stream, 420);
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Could not show {path}", ex);
                }
            };
            image.DetachedFromVisualTree += (_, _) =>
            {
                (image.Source as IDisposable)?.Dispose();
                image.Source = null;
            };
            // A click picks the message; a double click opens the picture.
            ToolTip.SetTip(image, "Double-click to open");
            image.DoubleTapped += (_, _) => OpenPath(path);
            panel.Children.Add(image);
        }

        var button = new Button { HorizontalAlignment = HorizontalAlignment.Left };
        button.Classes.Add("pill");
        button.Classes.Add("amber");
        var length = duration is { } d ? $" · {TimeSpan.FromSeconds(Math.Round(d)):m\\:ss}" : "";
        button.Content = ActionIcons.Label(ActionIcons.Paperclip, $"{name} · {Format.Bytes(size ?? 0)}{length}", "amber");
        button.Click += (_, _) => OpenPath(path);
        panel.Children.Add(button);
        return panel;
    }

    private Control FileRow(StoredFile f)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("76,96,*,260,180,220,96"), Margin = new Thickness(8, 4) };
        void Cell(int col, string text, bool mono = false, IBrush? hue = null)
        {
            var t = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            if (mono) t.Classes.Add("mono");
            if (hue is not null) t.Foreground = hue;
            ToolTip.SetTip(t, text);
            Grid.SetColumn(t, col);
            grid.Children.Add(t);
        }
        grid.Children.Add(Preview(f));
        Cell(1, Format.Bytes(f.Size));
        Cell(2, f.References > 1 ? $"{f.Name}  (×{f.References})" : f.Name, mono: true);
        Cell(3, CommandLine.Standing(f), hue: f.Mark switch
        {
            ArchiveLibrary.Important => Palette.Lime.Brush600,
            ArchiveLibrary.Irrelevant => Palette.Orange.Brush600,
            _ => null,
        });
        Cell(4, f.Senders);
        Cell(5, f.Chats);
        Cell(6, f.FirstSent.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return grid;
    }

    /// <summary>A video's first frame, made by ffmpeg the first time its row is shown and kept.</summary>
    private Control VideoPreview(StoredFile f)
    {
        var image = new Image { Width = 64, Height = 64, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Left };
        image.AttachedToVisualTree += async (_, _) =>
        {
            if (image.Source is not null) return;
            var preview = await Task.Run(() => _library.VideoPreview(f.Sha256, f.Path));
            if (preview is null || image.Source is not null) return;
            try { image.Source = new Bitmap(preview); } catch (Exception) { /* no frame */ }
        };
        image.DetachedFromVisualTree += (_, _) =>
        {
            (image.Source as IDisposable)?.Dispose();
            image.Source = null;
        };
        return image;
    }

    /// <summary>A small look at a picture, decoded only while its row is on screen.</summary>
    private Control Preview(StoredFile f)
    {
        var source = f.Reduced is null ? f.Path : f.ThumbPath;
        var isVideo = f.Reduced is null && f.MediaType == "video";
        var isPicture = source is not null && (f.Reduced is not null || f.MediaType is "image" or "sticker" or "gif");
        if (isVideo) return VideoPreview(f);
        if (!isPicture)
        {
            var icon = new TextBlock
            {
                Text = ActionIcons.Glyph(f.MediaType == "video" ? "f03d" : f.MediaType == "audio" ? "f028" : "f15b"),
                FontFamily = ActionIcons.Solid,
                Foreground = Palette.Amber.Brush500,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return icon;
        }
        var image = new Image { Width = 64, Height = 64, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Left };
        image.AttachedToVisualTree += (_, _) =>
        {
            if (image.Source is not null || !File.Exists(source)) return;
            try
            {
                using var stream = File.OpenRead(source!);
                image.Source = Bitmap.DecodeToWidth(stream, 128);
            }
            catch (Exception)
            {
                // Not every file decodes (HEIC); the row stays without a preview.
            }
        };
        image.DetachedFromVisualTree += (_, _) =>
        {
            (image.Source as IDisposable)?.Dispose();
            image.Source = null;
        };
        return image;
    }
}
