using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Vakage.Core.Library;
using Vakage.Design;

namespace Vakage.Views;

/// <summary>What the settings of a conversation asked for.</summary>
internal enum ChatSettingsChoice
{
    None,
    ToggleNoisy,
    Separate,
    Delete,
}

/// <summary>
/// A conversation's settings: the things done to it once, if ever (noisy,
/// separate a person's chats, delete), kept off the conversation's header.
/// </summary>
internal static class ChatSettings
{
    public static async Task<ChatSettingsChoice> Show(Window owner, Conversation conversation, IReadOnlyList<Chat> chats)
    {
        var choice = ChatSettingsChoice.None;
        var noisy = chats.Count > 0 && chats.All(c => c.Noisy);
        var window = new Window
        {
            Title = $"{conversation.Title} · settings",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var stack = new StackPanel { Spacing = 16, MaxWidth = 560 };
        var heading = new TextBlock { Text = conversation.Title };
        heading.Classes.Add("heading");
        stack.Children.Add(heading);
        var facts = new TextBlock
        {
            Text = $"{conversation.MessageCount:N0} messages, {conversation.First:d MMM yyyy} to {conversation.Last:d MMM yyyy}" +
                   (conversation.IsJoined ? $", in {chats.Count} chat histories: " + string.Join(", ", chats.Select(c => c.Title)) : "") + ".",
            TextWrapping = TextWrapping.Wrap,
        };
        facts.Classes.Add("caption");
        stack.Children.Add(facts);

        stack.Children.Add(Setting(
            noisy ? "This conversation is noisy" : "Noisy",
            "For a chat of daily forwards (greetings, quotes, memes). Its pictures are pruned hard: what looks like a real photo (a camera's shape, no words) is kept as important, voice notes too, and the rest suggested irrelevant.",
            ActionIcons.VolumeHigh, noisy ? "Not noisy" : "Mark noisy", "orange", noisy,
            () => { choice = ChatSettingsChoice.ToggleNoisy; window.Close(); }));

        if (conversation.IsJoined)
            stack.Children.Add(Setting(
                "The same person",
                "These chat histories are tied together as one person. Separate lists them apart again; nothing in them changes.",
                ActionIcons.LinkSlash, "Separate", "blue", false,
                () => { choice = ChatSettingsChoice.Separate; window.Close(); }));

        stack.Children.Add(Setting(
            "Delete",
            "Removes the conversation from the archive for good, with every file no other conversation uses (files marked important stay). Later imports leave it out.",
            ActionIcons.TrashCan, "Delete conversation", "orange", false,
            () => { choice = ChatSettingsChoice.Delete; window.Close(); }));

        var close = new Button { HorizontalAlignment = HorizontalAlignment.Right, Content = ActionIcons.Label(ActionIcons.Xmark, "Close", "slate") };
        close.Classes.Add("pill");
        close.Classes.Add("slate");
        close.Click += (_, _) => window.Close();
        stack.Children.Add(close);

        var section = new Border { Child = stack, Margin = new Thickness(12) };
        section.Classes.Add("section");
        var page = new Border { Child = section };
        page.Classes.Add("page");
        window.Content = page;
        await window.ShowDialog(owner);
        return choice;
    }

    private static Control Setting(string title, string text, string glyph, string action, string hue, bool active, Action act)
    {
        var name = new TextBlock { Text = title };
        name.Classes.Add("subheading");
        var about = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        about.Classes.Add("caption");
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Left, Content = ActionIcons.Label(glyph, action, hue) };
        button.Classes.Add("pill");
        button.Classes.Add(hue);
        button.Classes.Set("active", active);
        button.Click += (_, _) => act();
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(name);
        panel.Children.Add(about);
        panel.Children.Add(button);
        return panel;
    }
}
