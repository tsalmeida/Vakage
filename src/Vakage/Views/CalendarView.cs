using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Vakage.Design;

namespace Vakage.Views;

/// <summary>
/// A conversation's calendar: one heat map per year, a square per day,
/// coloured by how much was said that day against the conversation's own
/// typical day (the median of the days anything was said). Clicking a day
/// closes the calendar and takes the stream to it.
/// </summary>
internal static class CalendarView
{
    private const double Cell = 14, Gap = 3;

    public static async Task<DateTime?> Pick(Window owner, string title, IReadOnlyDictionary<DateTime, int> days)
    {
        DateTime? picked = null;
        var active = days.Values.Where(v => v > 0).OrderBy(v => v).ToList();
        var median = active.Count == 0 ? 1 : Math.Max(1, active[active.Count / 2]);

        var heading = new TextBlock { Text = title };
        heading.Classes.Add("heading");
        var caption = new TextBlock
        {
            Text = $"{days.Values.Sum():N0} messages on {active.Count:N0} days. A typical day here has {median:N0}; the darker a day, the more above that. Click a day to go to it.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 860,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        caption.Classes.Add("caption");

        var window = new Window
        {
            Title = $"{title} · calendar",
            Width = 1000,
            Height = 720,
            MinWidth = 600,
            MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var years = new StackPanel { Spacing = 20 };
        if (days.Count > 0)
        {
            for (var year = days.Keys.Max().Year; year >= days.Keys.Min().Year; year--)
                years.Children.Add(Year(year, days, median, day => { picked = day; window.Close(); }));
        }

        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(heading);
        stack.Children.Add(caption);
        stack.Children.Add(Legend(median));
        stack.Children.Add(years);

        var section = new Border { Child = new ScrollViewer { Content = new Border { Child = stack, Padding = new Thickness(16) } }, Margin = new Thickness(12), Padding = new Thickness(0) };
        section.Classes.Add("section");
        var page = new Border { Child = section };
        page.Classes.Add("page");
        window.Content = page;
        await window.ShowDialog(owner);
        return picked;
    }

    private static Control Year(int year, IReadOnlyDictionary<DateTime, int> days, int median, Action<DateTime> pick)
    {
        var total = Enumerable.Range(0, DateTime.IsLeapYear(year) ? 366 : 365).Sum(i => days.GetValueOrDefault(new DateTime(year, 1, 1).AddDays(i)));
        var label = new TextBlock { Text = $"{year} · {total:N0} messages" };
        label.Classes.Add("subheading");

        var canvas = new Canvas { Height = 18 + 7 * (Cell + Gap), Width = 30 + 54 * (Cell + Gap) };
        var first = new DateTime(year, 1, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7; // weeks start on Monday
        foreach (var (name, row) in new[] { ("Mon", 0), ("Wed", 2), ("Fri", 4) })
        {
            var t = new TextBlock { Text = name, FontSize = Typography.Floor };
            t.Classes.Add("caption");
            Canvas.SetLeft(t, 0);
            Canvas.SetTop(t, 18 + row * (Cell + Gap) - 2);
            canvas.Children.Add(t);
        }
        for (var day = first; day.Year == year; day = day.AddDays(1))
        {
            var index = (day - first).Days + offset;
            int column = index / 7, row = index % 7;
            if (day.Day == 1)
            {
                var month = new TextBlock { Text = day.ToString("MMM", CultureInfo.CurrentCulture), FontSize = Typography.Floor };
                month.Classes.Add("caption");
                Canvas.SetLeft(month, 30 + column * (Cell + Gap));
                Canvas.SetTop(month, 0);
                canvas.Children.Add(month);
            }
            var count = days.GetValueOrDefault(day);
            var square = new Border { Width = Cell, Height = Cell, CornerRadius = new CornerRadius(3) };
            if (count == 0)
                square[!Border.BackgroundProperty] = square.GetResourceObservable("TkxBorder").ToBinding();
            else
            {
                square.Background = Shade(count, median);
                var d = day;
                square.Cursor = new Cursor(StandardCursorType.Hand);
                square.PointerPressed += (_, _) => pick(d);
            }
            ToolTip.SetTip(square, $"{day:dddd, d MMMM yyyy}: {(count == 0 ? "nothing" : count == 1 ? "1 message" : $"{count:N0} messages")}");
            Canvas.SetLeft(square, 30 + column * (Cell + Gap));
            Canvas.SetTop(square, 18 + row * (Cell + Gap));
            canvas.Children.Add(square);
        }
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(label);
        panel.Children.Add(canvas);
        return panel;
    }

    /// <summary>Up to the typical day, then twice, four and eight times it.</summary>
    private static IBrush Shade(int count, int median) =>
        count <= median ? Palette.Indigo.Brush100
        : count <= 2 * median ? Palette.Indigo.Brush300
        : count <= 4 * median ? Palette.Indigo.Brush500
        : Palette.Indigo.Brush600;

    private static Control Legend(int median)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        void Add(IBrush? brush, string text)
        {
            var square = new Border { Width = Cell, Height = Cell, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
            if (brush is null) square[!Border.BackgroundProperty] = square.GetResourceObservable("TkxBorder").ToBinding();
            else square.Background = brush;
            var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            t.Classes.Add("caption");
            row.Children.Add(square);
            row.Children.Add(t);
        }
        Add(null, "nothing");
        Add(Palette.Indigo.Brush100, $"up to {median:N0}");
        Add(Palette.Indigo.Brush300, $"up to {2 * median:N0}");
        Add(Palette.Indigo.Brush500, $"up to {4 * median:N0}");
        Add(Palette.Indigo.Brush600, $"more than {4 * median:N0}");
        return row;
    }
}
