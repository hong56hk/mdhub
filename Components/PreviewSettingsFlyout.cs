using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Mdhub.Localization;
using Mdhub.Services;

namespace Mdhub.Components;

// Item 35: the preview gear's flyout. Every change applies live (PreviewSettings.Changed) and is saved in the session.
public static class PreviewSettingsFlyout
{
    public static Flyout Create()
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        void Fill() => flyout.Content = Build(Fill);
        flyout.Opening += (_, _) => Fill();
        return flyout;
    }

    private static readonly Lazy<string[]> AllFonts = new(() =>
    {
        try { return FontManager.Current.SystemFonts.Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray(); }
        catch (Exception) { return []; }
    });
    private static readonly Lazy<string[]> MonoFonts = new(() =>
    {
        var list = AllFonts.Value.Where(IsMonospace).ToList();
        if (!list.Contains(PreviewSettings.DefaultCodeFamily, StringComparer.OrdinalIgnoreCase)) list.Insert(0, PreviewSettings.DefaultCodeFamily);
        return [.. list.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)];
    });
    public static IReadOnlyList<string> BodyFonts => AllFonts.Value;
    public static IReadOnlyList<string> CodeFonts => MonoFonts.Value;

    // Narrow and wide letters take the same advance in a monospace font.
    public static bool IsMonospace(string family)
    {
        try
        {
            var typeface = new Typeface(new FontFamily(family));
            if (!string.Equals(typeface.GlyphTypeface.FamilyName, family, StringComparison.OrdinalIgnoreCase)) return false; // fell back
            using var narrow = new TextLayout("iiiiii", typeface, 20, null);
            using var wide = new TextLayout("MMMMMM", typeface, 20, null);
            return narrow.WidthIncludingTrailingWhitespace > 1 && Math.Abs(narrow.WidthIncludingTrailingWhitespace - wide.WidthIncludingTrailingWhitespace) < 0.5;
        }
        catch (Exception) { return false; }
    }

    public static StackPanel Build(Action? rebuild)
    {
        var s = PreviewSettings.Current;
        var panel = new StackPanel { Name = "PreviewSettingsPanel", Spacing = 6, Width = 280 };
        TextBlock Label(string key) => new() { Text = Loc.T(key), Foreground = ThemeService.Brush("TextSecondary"), Margin = new Thickness(0, 4, 0, 0) };
        NumericUpDown Number(string name, double value, double min, double max, double step, string format, Func<PreviewSettingsData, double, PreviewSettingsData> set)
        {
            var box = new NumericUpDown { Name = name, Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)step, FormatString = format, Value = (decimal)value, MinWidth = 110 };
            box.ValueChanged += (_, e) => { if (e.NewValue is { } v) PreviewSettings.Update(d => set(d, (double)v)); };
            return box;
        }
        ComboBox Fonts(string name, IReadOnlyList<string> fonts, string? selected, string? defaultLabel, Func<PreviewSettingsData, string?, PreviewSettingsData> set)
        {
            var items = new List<string>();
            if (defaultLabel is not null) items.Add(defaultLabel);
            items.AddRange(fonts);
            if (selected is not null && !items.Contains(selected, StringComparer.OrdinalIgnoreCase)) items.Add(selected);
            var box = new ComboBox { Name = name, ItemsSource = items, HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 320 };
            box.SelectedIndex = selected is null ? 0 : items.FindIndex(i => string.Equals(i, selected, StringComparison.OrdinalIgnoreCase));
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is not string pick) return;
                PreviewSettings.Update(d => set(d, defaultLabel is not null && box.SelectedIndex == 0 ? null : pick));
            };
            return box;
        }
        Grid Row(Control family, Control size)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
            grid.Children.Add(family); Grid.SetColumn(size, 1); grid.Children.Add(size);
            return grid;
        }

        panel.Children.Add(new TextBlock { Text = Loc.T("PreviewSettings"), FontWeight = FontWeight.SemiBold });
        panel.Children.Add(Label("PreviewBodyFont"));
        panel.Children.Add(Row(Fonts("BodyFontBox", BodyFonts, s.BodyFamily, Loc.T("PreviewDefaultFont"), (d, f) => d with { BodyFamily = f }),
            Number("BodySizeBox", s.BodySize, PreviewSettings.MinBodySize, PreviewSettings.MaxBodySize, 1, "0", (d, v) => d with { BodySize = v })));
        panel.Children.Add(Label("PreviewCodeFont"));
        panel.Children.Add(Row(Fonts("CodeFontBox", CodeFonts, s.CodeFamily, null, (d, f) => d with { CodeFamily = f ?? PreviewSettings.DefaultCodeFamily }),
            Number("CodeSizeBox", s.CodeSize, PreviewSettings.MinCodeSize, PreviewSettings.MaxCodeSize, 1, "0", (d, v) => d with { CodeSize = v })));
        panel.Children.Add(Label("PreviewLineHeight"));
        panel.Children.Add(Number("LineHeightBox", s.LineHeight, PreviewSettings.MinLineHeight, PreviewSettings.MaxLineHeight, 0.1, "0.0", (d, v) => d with { LineHeight = v }));
        panel.Children.Add(Label("PreviewParagraphSpacing"));
        panel.Children.Add(Number("ParagraphSpacingBox", s.ParagraphSpacing, PreviewSettings.MinParagraphSpacing, PreviewSettings.MaxParagraphSpacing, 2, "0", (d, v) => d with { ParagraphSpacing = v }));
        panel.Children.Add(Label("PreviewBulletSpacing"));
        panel.Children.Add(Number("BulletSpacingBox", s.BulletSpacing, PreviewSettings.MinListSpacing, PreviewSettings.MaxListSpacing, 1, "0", (d, v) => d with { BulletSpacing = v }));
        panel.Children.Add(Label("PreviewOrderedSpacing"));
        panel.Children.Add(Number("OrderedSpacingBox", s.OrderedSpacing, PreviewSettings.MinListSpacing, PreviewSettings.MaxListSpacing, 1, "0", (d, v) => d with { OrderedSpacing = v }));
        var reset = new Button { Name = "ResetPreviewSettings", Content = Loc.T("PreviewResetDefaults"), Classes = { "textAction" }, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        reset.Click += (_, _) => { PreviewSettings.Reset(); rebuild?.Invoke(); };
        panel.Children.Add(reset);
        return panel;
    }
}