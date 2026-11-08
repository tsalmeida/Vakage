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

    private void ReloadSelf()
    {
        _loading = true;
        if (_account is null)
        {
            SelfBox.ItemsSource = null;
            _selfName = null;
        }
        else
        {
            _selfName = _library.SelfNameFor(_account.Id);
            var senders = _library.Senders(_account.Id).ToList();
            SelfBox.ItemsSource = senders;
            SelfBox.SelectedItem = _selfName;
        }
        _loading = false;
    }

    private void OnSelfChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || _account is null || SelfBox.SelectedItem is not string name) return;
        _library.SetSelfName(_account.Id, name);
        _selfName = name;
        ShowSelectedConversation();
    }

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
        ImportFileButton.IsEnabled = ImportFolderButton.IsEnabled = true;
        ReloadSelf();
        ReloadConversations();
        ReloadFiles();
        Status(string.Join("  ", lines));
    }

    // ----- Conversations -----

    private void ReloadConversations()
    {
        var conversations = _account is null ? [] : _library.Conversations(_account.Id);
        ConversationList.ItemsSource = conversations;
        ConversationsCaption.Text = _account is null
            ? "Add an account with New account, then import its exported chats."
            : conversations.Count == 0
                ? $"No chats in {_account.Name} yet. Export a chat on the phone (Export chat, Include media) and import the zip."
                : $"{conversations.Count} in {_account.Name}. Ctrl+click to select several, to join or delete them.";
        if (conversations.Count > 0) ConversationList.SelectedIndex = 0;
        else ShowSelectedConversation();
    }

    private void OnConversationSelectionChanged(object? sender, SelectionChangedEventArgs e) => ShowSelectedConversation();

    private List<Conversation> SelectedConversations()
        => ConversationList.SelectedItems?.OfType<Conversation>().ToList() ?? [];

    private void ShowSelectedConversation()
    {
        var selected = SelectedConversations();
        JoinButton.IsEnabled = selected.Count >= 2;
        UnjoinButton.IsEnabled = selected.Count == 1 && selected[0].IsJoined;
        DeleteButton.IsEnabled = selected.Count >= 1;

        if (selected.Count != 1)
        {
            ChatTitle.Text = selected.Count == 0 ? "" : $"{selected.Count} conversations selected";
            ChatCaption.Text = selected.Count == 0 ? "" : "Join reads them as one conversation; Delete removes them all.";
            MessageList.ItemsSource = null;
            return;
        }

        var c = selected[0];
        var messages = _library.Messages(c.ChatIds);
        ChatTitle.Text = c.Title;
        ChatCaption.Text = $"{_account?.Name}'s archive · {c.MessageCount:N0} messages · {c.First:d MMM yyyy} to {c.Last:d MMM yyyy}" +
                           (c.IsJoined ? $" · joined from {c.ChatIds.Count} chats" : "");
        _chatTitles = c.IsJoined
            ? _library.Chats(_account!.Id).Where(x => c.ChatIds.Contains(x.Id)).ToDictionary(x => x.Id, x => x.Title)
            : null;

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
        MessageList.ItemsSource = rows;
        // Opens on the latest message, as a chat does; after layout, or it lands short.
        if (rows.Count > 0)
            Dispatcher.UIThread.Post(() => MessageList.ScrollIntoView(rows.Count - 1), DispatcherPriority.Background);
    }

    private Dictionary<long, string>? _chatTitles;

    private async void OnJoinClick(object? sender, RoutedEventArgs e)
    {
        var selected = SelectedConversations();
        if (selected.Count < 2) return;
        var title = await Dialogs.AskText(this, "Join conversations",
            "The joined conversation's name. The chats stay as they were imported, and Unjoin parts them again.",
            selected[0].Title);
        if (string.IsNullOrWhiteSpace(title)) return;
        try
        {
            _library.Join(selected.SelectMany(c => c.ChatIds).Distinct().ToList(), title);
            ReloadConversations();
            Status($"Joined {selected.Count} conversations as {title}.");
        }
        catch (Exception ex)
        {
            Status(ex.Message);
        }
    }

    private void OnUnjoinClick(object? sender, RoutedEventArgs e)
    {
        var selected = SelectedConversations();
        if (selected is not [{ ThreadId: { } thread } c]) return;
        _library.Unjoin(thread);
        ReloadConversations();
        Status($"{c.Title} is parted into its {c.ChatIds.Count} chats again.");
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        var selected = SelectedConversations();
        if (selected.Count == 0 || _account is null) return;
        var names = string.Join(", ", selected.Select(c => c.Title));
        var ok = await Dialogs.Confirm(this, "Delete conversations",
            $"Delete {names} from {_account.Name}'s archive? Their messages go, and so does every file no other conversation uses. " +
            "This cannot be undone; the original exports are not touched.",
            "Delete");
        if (!ok) return;
        var freed = _library.DeleteChats(selected.SelectMany(c => c.ChatIds).ToList());
        ReloadSelf();
        ReloadConversations();
        ReloadFiles();
        Status($"Deleted {names}; {Format.Bytes(freed)} freed.");
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
        var files = _library.Files(_account.Id, person, exclude);
        FileList.ItemsSource = files;
        FilesCaption.Text = $"{files.Count:N0} files, {Format.Bytes(files.Sum(f => f.Size))}, in {_account.Name}'s archive, largest first. " +
                            "A file sent in several chats is stored, and listed, once.";
        OpenFileButton.IsEnabled = false;
    }

    private void OnFileSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => OpenFileButton.IsEnabled = FileList.SelectedItem is StoredFile;

    private void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is StoredFile f) OpenPath(f.Path);
    }

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FileList.SelectedItem is StoredFile f) OpenPath(f.Path);
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

        var mine = _selfName is not null && m.Sender == _selfName;
        var content = new StackPanel { Spacing = 4 };

        var meta = new TextBlock
        {
            Text = $"{m.Sender} · {m.SentAt:HH:mm}" +
                   (_chatTitles is not null && _chatTitles.TryGetValue(m.ChatId, out var from) ? $" · {from}" : ""),
            TextWrapping = TextWrapping.Wrap,
        };
        meta.Classes.Add("caption");
        content.Children.Add(meta);

        if (m.Kind == MessageKind.MediaOmitted)
        {
            var omitted = new TextBlock { Text = "A file the export left out.", FontStyle = FontStyle.Italic };
            omitted.Classes.Add("caption");
            content.Children.Add(omitted);
        }
        else if (m.AttachmentName is { } attachment)
        {
            content.Children.Add(Attachment(attachment, m.FilePath, m.FileSize));
        }

        if (m.Body.Length > 0)
        {
            var body = new SelectableTextBlock { Text = m.Body, TextWrapping = TextWrapping.Wrap };
            body.Classes.Add("prose");
            content.Children.Add(body);
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

    private Control Attachment(string name, string? path, long? size)
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
            image.Cursor = new Cursor(StandardCursorType.Hand);
            image.PointerPressed += (_, _) => OpenPath(path);
            panel.Children.Add(image);
        }

        var button = new Button { HorizontalAlignment = HorizontalAlignment.Left };
        button.Classes.Add("pill");
        button.Classes.Add("amber");
        button.Content = ActionIcons.Label(ActionIcons.Paperclip, $"{name} · {Format.Bytes(size ?? 0)}", "amber");
        button.Click += (_, _) => OpenPath(path);
        panel.Children.Add(button);
        return panel;
    }

    private static Control FileRow(StoredFile f)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*,240,280,110"), Margin = new Thickness(8, 4) };
        void Cell(int col, string text, bool mono = false)
        {
            var t = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0) };
            if (mono) t.Classes.Add("mono");
            Grid.SetColumn(t, col);
            grid.Children.Add(t);
        }
        Cell(0, Format.Bytes(f.Size));
        Cell(1, f.References > 1 ? $"{f.Name}  (×{f.References})" : f.Name, mono: true);
        Cell(2, f.Senders);
        Cell(3, f.Chats);
        Cell(4, f.FirstSent.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return grid;
    }
}
