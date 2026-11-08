using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Vakage.Design;

namespace Vakage.Views;

/// <summary>
/// The app's own small dialogs, drawn in the house style (never a system
/// message box): a question with one text field, and a confirmation.
/// </summary>
internal static class Dialogs
{
    public static async Task<string?> AskText(Window owner, string title, string question, string initial = "")
    {
        var box = new TextBox { Text = initial, MinWidth = 360 };
        string? result = null;
        var window = Frame(title, question, box, out var ok, out var cancel, "Save", ActionIcons.Check);
        ok.Click += (_, _) => { result = box.Text; window.Close(); };
        cancel.Click += (_, _) => window.Close();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { result = box.Text; window.Close(); }
            if (e.Key == Key.Escape) window.Close();
        };
        window.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        await window.ShowDialog(owner);
        return result?.Trim();
    }

    public static async Task<bool> Confirm(Window owner, string title, string question, string confirmLabel, bool destructive = true)
    {
        var confirmed = false;
        var window = Frame(title, question, null, out var ok, out var cancel, confirmLabel,
            destructive ? ActionIcons.TrashCan : ActionIcons.FileImport, negative: destructive);
        ok.Click += (_, _) => { confirmed = true; window.Close(); };
        cancel.Click += (_, _) => window.Close();
        await window.ShowDialog(owner);
        return confirmed;
    }

    private static Window Frame(string title, string question, Control? field, out Button ok, out Button cancel,
        string okLabel, string okGlyph, bool negative = false)
    {
        var text = new TextBlock { Text = question, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 520 };
        var heading = new TextBlock { Text = title };
        heading.Classes.Add("heading");

        // A destructive confirmation is the orange pill, and its way out the
        // grey one; a positive one is lime, with cancel in orange.
        var okHue = negative ? "orange" : "lime";
        ok = new Button { Content = ActionIcons.Label(okGlyph, okLabel, okHue) };
        ok.Classes.Add("pill");
        ok.Classes.Add(okHue);
        cancel = new Button { Content = ActionIcons.Label(ActionIcons.Xmark, "Cancel", negative ? "slate" : "orange") };
        cancel.Classes.Add("pill");
        cancel.Classes.Add(negative ? "slate" : "orange");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(heading);
        stack.Children.Add(text);
        if (field is not null) stack.Children.Add(field);
        stack.Children.Add(buttons);

        var section = new Border { Child = stack, Margin = new Thickness(12) };
        section.Classes.Add("section");
        var page = new Border { Child = section };
        page.Classes.Add("page");

        return new Window
        {
            Title = title,
            Content = page,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Icon = null,
        };
    }
}
