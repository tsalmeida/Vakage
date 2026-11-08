using System.Globalization;
using Avalonia.Media;

namespace Vakage.Design;

/// <summary>
/// The type scale of the companion apps: one family (Roboto), a 12 px floor,
/// prose at 21/32 held to a 70-character measure. Everything that is prose
/// goes through <see cref="ColumnWidth"/> so the column follows the type.
/// </summary>
public static class Typography
{
    public static readonly FontFamily Sans = new("avares://Vakage/Assets/Fonts#Roboto");
    public static readonly FontFamily Slab = new("avares://Vakage/Assets/Fonts#Roboto Slab");
    public static readonly FontFamily Condensed = new("avares://Vakage/Assets/Fonts#Roboto Condensed");
    public static readonly FontFamily Mono = new("avares://Vakage/Assets/Fonts#Roboto Mono");

    /// <summary>Captions and metadata (Condensed). 12 px is the floor; nothing is set smaller.</summary>
    public const double Caption = 14;

    public const double Floor = 12;

    /// <summary>Buttons, tabs, labels, rows, inputs, menus.</summary>
    public const double Interface = 16;

    /// <summary>Running text: messages, notes, descriptions.</summary>
    public const double Prose = 21;

    public const double Subheading = 18;
    public const double Heading = 21;
    public const double Title = 26;

    /// <summary>Baseline to baseline for prose, as a multiple of the size.</summary>
    public const double ProseLineSpacing = 1.5;

    /// <summary>Characters to a line — the measure prose is held to.</summary>
    public const int Measure = 70;

    public static double Leading(double fontSize) => Math.Round(fontSize * ProseLineSpacing);

    /// <summary>
    /// A line of ordinary English, used to find out how wide a character is. A
    /// proportional face has no one character width; CSS averages by the zero,
    /// which is among the widest glyphs and yields a column of nearly eighty
    /// letters. A sentence, with letters in the proportion English uses them,
    /// gives a column that holds what seventy characters means to a reader.
    /// (Ported from Mnemarchos's Writing.cs.)
    /// </summary>
    private const string Sample =
        "It was a quiet morning and there was little to say about it, so I "
        + "wrote down the weather and the walk instead, and left the rest.";

    /// <summary>Pixel width of a <see cref="Measure"/>-character column in the given face and size.</summary>
    public static double ColumnWidth(FontFamily family, double fontSize)
    {
        var sample = new FormattedText(
            Sample, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family), fontSize, Brushes.Black);

        return Math.Ceiling(sample.Width / Sample.Length * Measure);
    }
}
