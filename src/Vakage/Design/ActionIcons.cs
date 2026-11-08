using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Vakage.Design;

/// <summary>
/// Font Awesome glyphs used by Vakage's actions. Vakage is open source, so it
/// embeds Font Awesome Free's Solid face (SIL OFL 1.1, icons CC BY 4.0; see
/// Assets/Fonts/FontAwesome-LICENSE.txt), not the Pro face the private
/// companions carry. Every glyph here must exist in the Free set.
/// </summary>
public static class ActionIcons
{
    public static readonly FontFamily Solid =
        new("avares://Vakage/Assets/Fonts#Font Awesome 6 Free Solid");

    public const string ArrowDownWideShort = "f160";
    public const string BoxArchive = "f187";
    public const string Check = "f00c";
    public const string Comments = "f086";
    public const string FileImport = "f56f";
    public const string FolderOpen = "f07c";
    public const string Link = "f0c1";
    public const string LinkSlash = "f127";
    public const string Paperclip = "f0c6";
    public const string Rotate = "f2f1";
    public const string TrashCan = "f2ed";
    public const string UserPlus = "f234";
    public const string Xmark = "f00d";

    public static string Glyph(string codepoint) =>
        char.ConvertFromUtf32(Convert.ToInt32(codepoint, 16));

    public static ActionIconLabel Label(string glyph, string? text = null, string? hue = null) => new()
    {
        Glyph = glyph,
        Text = text,
        Hue = hue,
    };

    public static string ForDialogButton(string label) => label switch
    {
        "No" or "Cancel" => Xmark,
        _ => Check,
    };
}

/// <summary>A Font Awesome action icon followed by an optional plain-language label.</summary>
public sealed class ActionIconLabel : StackPanel
{
    public static readonly StyledProperty<string?> GlyphProperty =
        AvaloniaProperty.Register<ActionIconLabel, string?>(nameof(Glyph));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ActionIconLabel, string?>(nameof(Text));

    public static readonly StyledProperty<string?> HueProperty =
        AvaloniaProperty.Register<ActionIconLabel, string?>(nameof(Hue));

    private readonly TextBlock _icon;
    private readonly TextBlock _label;

    public ActionIconLabel()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Center;

        _icon = new TextBlock
        {
            FontFamily = ActionIcons.Solid,
            FontSize = Typography.Interface,
            Width = 20,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _label = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };
        _label.Classes.Add("compact");

        Children.Add(_icon);
        Children.Add(_label);
    }

    public string? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Hue
    {
        get => GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GlyphProperty || change.Property == TextProperty || change.Property == HueProperty)
            Apply();
    }

    private void Apply()
    {
        _icon.Text = string.IsNullOrWhiteSpace(Glyph) ? "" : ActionIcons.Glyph(Glyph);
        _icon.IsVisible = _icon.Text.Length > 0;
        _icon.Foreground = Palette.ByName(Hue)?.Brush500;
        _label.Text = Text ?? "";
        _label.IsVisible = _label.Text.Length > 0;
        Spacing = _icon.IsVisible && _label.IsVisible ? 8 : 0;
    }
}
