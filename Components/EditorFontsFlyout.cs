using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Mdhub.Localization;
using Mdhub.Services;

namespace Mdhub.Components;

// Item 57: the editor gear's flyout: exactly two installed-font pickers (body and code). Changes apply live.
public static class EditorFontsFlyout
{
    public static Flyout Create()
    {
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Opening += (_, _) => flyout.Content = Build();
        return flyout;
    }

    public static IReadOnlyList<string> Fonts(string current)
    {
        var fonts = PreviewSettingsFlyout.BodyFonts.ToList();
        foreach (var name in new[] { EditorFonts.DefaultFamily, current })
            if (!fonts.Contains(name, StringComparer.OrdinalIgnoreCase)) fonts.Add(name);
        return [.. fonts.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)];
    }

    public static StackPanel Build()
    {
        var s = EditorFonts.Current;
        var panel = new StackPanel { Name = "EditorFontsPanel", Spacing = 6, Width = 280 };
        TextBlock Label(string key) => new() { Text = Loc.T(key), Foreground = ThemeService.Brush("TextSecondary"), Margin = new Thickness(0, 4, 0, 0) };
        ComboBox Picker(string name, string selected, Func<EditorFontsData, string, EditorFontsData> set)
        {
            var items = Fonts(selected);
            var box = new ComboBox { Name = name, ItemsSource = items, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, MaxDropDownHeight = 320 };
            box.SelectedIndex = items.ToList().FindIndex(i => string.Equals(i, selected, StringComparison.OrdinalIgnoreCase));
            box.SelectionChanged += (_, _) => { if (box.SelectedItem is string pick) EditorFonts.Update(d => set(d, pick)); };
            return box;
        }
        panel.Children.Add(new TextBlock { Text = Loc.T("EditorFontSettings"), FontWeight = Avalonia.Media.FontWeight.SemiBold });
        panel.Children.Add(Label("EditorBodyFont"));
        panel.Children.Add(Picker("EditorBodyFontBox", s.Body, (d, f) => d with { Body = f }));
        panel.Children.Add(Label("EditorCodeFont"));
        panel.Children.Add(Picker("EditorCodeFontBox", s.Code, (d, f) => d with { Code = f }));
        return panel;
    }
}
