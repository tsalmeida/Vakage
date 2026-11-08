using Avalonia.Media;

namespace Vakage.Design;

/// <summary>
/// Tailwind's palette at the steps the companion apps use — the same hues at
/// the same steps as the Tekhnexos site (see documents/companion-design-standard.md
/// in the Tekhnexos repository). Use the names; never write an ad-hoc hex.
///
/// Lime means positive and orange means negative, and nothing else is lime or
/// orange. Every area has its hue (AI characters indigo, Habit Tracker cyan,
/// constructs blue, …). An app's identity colour lives on its icon only.
/// </summary>
public static class Palette
{
    public sealed record Hue(string Name, Color S50, Color S100, Color S300, Color S500, Color S600, Color S950)
    {
        public IBrush Brush50 { get; } = new ImmutableSolid(S50);
        public IBrush Brush100 { get; } = new ImmutableSolid(S100);
        public IBrush Brush300 { get; } = new ImmutableSolid(S300);
        public IBrush Brush500 { get; } = new ImmutableSolid(S500);
        public IBrush Brush600 { get; } = new ImmutableSolid(S600);
        public IBrush Brush950 { get; } = new ImmutableSolid(S950);
    }

    private sealed class ImmutableSolid(Color c) : Avalonia.Media.Immutable.ImmutableSolidColorBrush(c);

    private static Color C(string hex) => Color.Parse(hex);

    // Areas
    public static readonly Hue Indigo = new("indigo", C("#eef2ff"), C("#e0e7ff"), C("#a3b3ff"), C("#615fff"), C("#4f39f6"), C("#1e1a4d"));
    public static readonly Hue Cyan = new("cyan", C("#ecfeff"), C("#cefafe"), C("#53eafd"), C("#00b8db"), C("#0092b8"), C("#053345"));
    public static readonly Hue Blue = new("blue", C("#eff6ff"), C("#dbeafe"), C("#8ec5ff"), C("#2b7fff"), C("#155dfc"), C("#162456"));
    public static readonly Hue Green = new("green", C("#f0fdf4"), C("#dcfce7"), C("#7bf1a8"), C("#00c950"), C("#00a63e"), C("#032e15"));
    public static readonly Hue Pink = new("pink", C("#fdf2f8"), C("#fce7f3"), C("#fda5d5"), C("#f6339a"), C("#e60076"), C("#510424"));
    public static readonly Hue Amber = new("amber", C("#fffbeb"), C("#fef3c6"), C("#ffd230"), C("#fe9a00"), C("#e17100"), C("#461901"));
    public static readonly Hue Purple = new("purple", C("#faf5ff"), C("#f3e8ff"), C("#dab2ff"), C("#ad46ff"), C("#9810fa"), C("#3c0366"));
    public static readonly Hue Emerald = new("emerald", C("#ecfdf5"), C("#d0fae5"), C("#5ee9b5"), C("#00bc7d"), C("#009966"), C("#002c22"));
    public static readonly Hue Teal = new("teal", C("#f0fdfa"), C("#cbfbf1"), C("#46ecd5"), C("#00bba7"), C("#009689"), C("#022f2e"));

    // The areas a character keeps (the site's Tailwind names; see config/icons.php there)
    public static readonly Hue Sky = new("sky", C("#f0f9ff"), C("#dff2fe"), C("#74d4ff"), C("#00a6f4"), C("#0084d1"), C("#052f4a"));
    public static readonly Hue Violet = new("violet", C("#f5f3ff"), C("#ede9fe"), C("#c4b4ff"), C("#8e51ff"), C("#7f22fe"), C("#2f0d68"));
    public static readonly Hue Rose = new("rose", C("#fff1f2"), C("#ffe4e6"), C("#ffa1ad"), C("#ff2056"), C("#ec003f"), C("#4d0218"));
    public static readonly Hue Fuchsia = new("fuchsia", C("#fdf4ff"), C("#fae8ff"), C("#f4a8ff"), C("#e12afb"), C("#c800de"), C("#4b004f"));

    /// <summary>A hue by the site's colour name, for a message's area; null when unknown.</summary>
    public static Hue? ByName(string? name) => name switch
    {
        "indigo" => Indigo, "cyan" => Cyan, "blue" => Blue, "green" => Green, "pink" => Pink, "amber" => Amber,
        "purple" => Purple, "emerald" => Emerald, "teal" => Teal, "lime" => Lime, "orange" => Orange,
        "slate" => Slate, "sky" => Sky, "violet" => Violet, "rose" => Rose, "fuchsia" => Fuchsia,
        _ => null,
    };

    /// <summary>The family's own hue: no area, no meaning, worn by Nauarchos.</summary>
    public static readonly Hue Slate = new("slate", C("#f8fafc"), C("#f1f5f9"), C("#cad5e2"), C("#62748e"), C("#45556c"), C("#020618"));

    // Meaning — reserved
    public static readonly Hue Lime = new("lime", C("#f7fee7"), C("#ecfcca"), C("#bbf451"), C("#7ccf00"), C("#5ea500"), C("#192e03"));
    public static readonly Hue Orange = new("orange", C("#fff7ed"), C("#ffedd4"), C("#ffb86a"), C("#ff6900"), C("#f54900"), C("#441306"));

    /// <summary>Vakage's identity colour (stone-600) — the icon, and nothing else.</summary>
    public static readonly Color Identity = C("#57534d");

    // The page — light
    public static readonly Color LightBase100 = C("#ffffff"); // sections
    public static readonly Color LightBase200 = C("#fafafa"); // page
    public static readonly Color LightBase300 = C("#f2f2f2"); // borders
    public static readonly Color LightText = C("#1e1e22");

    // The page — dark (warm, like the site)
    public static readonly Color DarkBase100 = C("#3a3532");
    public static readonly Color DarkBase200 = C("#2e2a27");
    public static readonly Color DarkBase300 = C("#1f1c19");
    public static readonly Color DarkText = C("#fafaf9");
}
