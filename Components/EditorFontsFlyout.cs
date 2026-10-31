using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Mdhub.Localization;
using Mdhub.Services;

namespace Mdhub.Components;

// Item 57: the editor gear's flyout: two installed-font pickers (body and code). Item 62 adds a Colors section
// below them (one compact row per colour: swatch picker + reset, then 'Reset colors'); the flyout scrolls when tall.
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

    public static ScrollViewer Build() => new()
    {
        Name = "EditorSettingsScroll", MaxHeight = 560, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Content = new StackPanel { Spacing = 6, Children = { BuildFonts(), BuildTabs(), BuildEncoding(), BuildColors() } },
    };

    private static TextBlock Label(string key) => new() { Text = Loc.T(key), Foreground = ThemeService.Brush("TextSecondary"), Margin = new Thickness(0, 4, 0, 0) };

    public static StackPanel BuildFonts()
    {
        var s = EditorFonts.Current;
        var panel = new StackPanel { Name = "EditorFontsPanel", Spacing = 6, Width = 280 };
        ComboBox Picker(string name, string selected, Func<EditorFontsData, string, EditorFontsData> set)
        {
            var items = Fonts(selected);
            var box = new ComboBox { Name = name, ItemsSource = items, HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 320 };
            box.SelectedIndex = items.ToList().FindIndex(i => string.Equals(i, selected, StringComparison.OrdinalIgnoreCase));
            box.SelectionChanged += (_, _) => { if (box.SelectedItem is string pick) EditorFonts.Update(d => set(d, pick)); };
            return box;
        }
        panel.Children.Add(new TextBlock { Text = Loc.T("EditorFontSettings"), FontWeight = FontWeight.SemiBold });
        panel.Children.Add(Label("EditorBodyFont"));
        panel.Children.Add(Picker("EditorBodyFontBox", s.Body, (d, f) => d with { Body = f }));
        panel.Children.Add(Label("EditorCodeFont"));
        panel.Children.Add(Picker("EditorCodeFontBox", s.Code, (d, f) => d with { Code = f }));
        return panel;
    }

    // Item 70: Tab behaviour: insert spaces (default on) and the tab size (1-8, default 4).
    public static StackPanel BuildTabs()
    {
        var s = EditorBehavior.Current;
        var panel = new StackPanel { Name = "EditorTabsPanel", Spacing = 6, Width = 280, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock { Text = Loc.T("EditorTabSettings"), FontWeight = FontWeight.SemiBold });
        var spaces = new CheckBox { Name = "EditorInsertSpacesBox", Content = Loc.T("EditorInsertSpaces"), IsChecked = s.InsertSpaces };
        spaces.IsCheckedChanged += (_, _) => EditorBehavior.Update(d => d with { InsertSpaces = spaces.IsChecked == true });
        panel.Children.Add(spaces);
        var size = new NumericUpDown
        {
            Name = "EditorTabSizeBox", Minimum = Mdhub.Text.TabEditing.MinSize, Maximum = Mdhub.Text.TabEditing.MaxSize, Increment = 1, FormatString = "0",
            Value = s.TabSize, Width = 110, HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(size, Loc.T("EditorTabSize"));
        size.ValueChanged += (_, e) => { if (e.NewValue is decimal v) EditorBehavior.Update(d => d with { TabSize = (int)Math.Round(v) }); };
        var row = new DockPanel { LastChildFill = false };
        var label = new TextBlock { Text = Loc.T("EditorTabSize"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 160 };
        DockPanel.SetDock(label, Dock.Left); DockPanel.SetDock(size, Dock.Right);
        row.Children.Add(label); row.Children.Add(size);
        panel.Children.Add(row);
        return panel;
    }

    // Item 67: the Default encoding used when a legacy file's encoding cannot be detected confidently.
    public static StackPanel BuildEncoding()
    {
        var panel = new StackPanel { Name = "EditorEncodingPanel", Spacing = 6, Width = 280, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock { Text = Loc.T("EncodingSettings"), FontWeight = FontWeight.SemiBold });
        panel.Children.Add(Label("DefaultEncoding"));
        var options = TextEncodings.All.Where(o => !o.Bom).ToList();
        var box = new ComboBox { Name = "DefaultEncodingBox", ItemsSource = options.Select(o => o.Name).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 320 };
        AutomationProperties.SetName(box, Loc.T("DefaultEncoding"));
        ToolTip.SetTip(box, Loc.T("DefaultEncodingTip"));
        box.SelectedIndex = options.FindIndex(o => o.Id == TextEncodings.DefaultEncodingId);
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) TextEncodings.SetDefault(options[box.SelectedIndex].Id); };
        panel.Children.Add(box);
        // Item 68: the line ending of new files (OS, CRLF or LF).
        panel.Children.Add(Label("DefaultLineEnding"));
        var endings = LineEndingSettings.Ids;
        var eolBox = new ComboBox
        {
            Name = "DefaultLineEndingBox", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = endings.Select(id => id == LineEndingSettings.DefaultId ? Loc.F("LineEndingOs", Mdhub.Text.LineEndings.Label(Mdhub.Text.LineEndings.Os)) : Mdhub.Text.LineEndings.Label(LineEndingSettings.Resolve(id))).ToList()
        };
        AutomationProperties.SetName(eolBox, Loc.T("DefaultLineEnding"));
        ToolTip.SetTip(eolBox, Loc.T("DefaultLineEndingTip"));
        eolBox.SelectedIndex = Array.IndexOf(endings, LineEndingSettings.DefaultLineEndingId);
        eolBox.SelectionChanged += (_, _) => { if (eolBox.SelectedIndex >= 0) LineEndingSettings.SetDefault(endings[eolBox.SelectedIndex]); };
        panel.Children.Add(eolBox);
        return panel;
    }

    // Counter-clockwise arrow (thin stroke, like the toolbar icons).
    private const string ResetGlyph = "M 3.5,7 A 4.5,4.5 0 1 0 5,3.6 M 5.2,1.2 V 3.8 H 2.6";

    public static StackPanel BuildColors()
    {
        var panel = new StackPanel { Name = "EditorColorsPanel", Spacing = 3, Width = 280, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock { Text = Loc.T("EditorColors"), FontWeight = FontWeight.SemiBold });
        var refreshers = new List<Action>();
        void RefreshAll() { foreach (var r in refreshers) r(); }
        foreach (var item in EditorColors.Items)
        {
            var label = Loc.T(item.Label);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 4 };
            var text = new TextBlock { Text = label, Foreground = ThemeService.Brush("TextSecondary"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var chip = new Border { Width = 34, Height = 16, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = ThemeService.Brush("PanelBorder") };
            var swatch = new Button { Name = "EditorColor" + item.Id, Classes = { "tool" }, Padding = new Thickness(4, 3), Content = chip, Flyout = PickerFlyout(item.Id, RefreshAll) };
            ToolTip.SetTip(swatch, label); AutomationProperties.SetName(swatch, label);
            var reset = new Button
            {
                Name = "EditorColorReset" + item.Id, Classes = { "tool" }, Padding = new Thickness(5, 3),
                Content = new Avalonia.Controls.Shapes.Path { Width = 12, Height = 12, Stretch = Stretch.None, StrokeThickness = 1.25, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Data = Geometry.Parse(ResetGlyph), Stroke = ThemeService.Brush("IconForeground") },
            };
            var resetName = $"{Loc.T("EditorColorReset")}: {label}";
            ToolTip.SetTip(reset, resetName); AutomationProperties.SetName(reset, resetName);
            reset.Click += (_, _) => { EditorColors.Reset(item.Id); RefreshAll(); };
            refreshers.Add(() => { chip.Background = new SolidColorBrush(EditorColors.Get(item.Id)); reset.IsEnabled = EditorColors.IsOverridden(item.Id); });
            Grid.SetColumn(swatch, 1); Grid.SetColumn(reset, 2);
            row.Children.Add(text); row.Children.Add(swatch); row.Children.Add(reset);
            panel.Children.Add(row);
        }
        var resetAll = new Button { Name = "ResetEditorColors", Content = Loc.T("ResetEditorColors"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        resetAll.Click += (_, _) => { EditorColors.ResetAll(); RefreshAll(); };
        refreshers.Add(() => resetAll.IsEnabled = EditorColors.HasOverrides());
        panel.Children.Add(resetAll);
        RefreshAll();
        return panel;
    }

    // A small picker: hex box plus red/green/blue sliders; every change applies live.
    public static Flyout PickerFlyout(string id, Action changed)
    {
        var flyout = new Flyout { Placement = PlacementMode.LeftEdgeAlignedTop };
        flyout.Opening += (_, _) => flyout.Content = BuildPicker(id, changed);
        return flyout;
    }

    public static StackPanel BuildPicker(string id, Action changed)
    {
        var panel = new StackPanel { Name = "EditorColorPicker", Spacing = 4, Width = 220 };
        var color = EditorColors.Get(id);
        var updating = false;
        var preview = new Border { Height = 22, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = ThemeService.Brush("PanelBorder") };
        var hex = new TextBox { Name = "EditorColorHex", MaxLength = 9 };
        AutomationProperties.SetName(hex, Loc.T("EditorColorHex"));
        var sliders = new List<Slider>();
        void Show(Color c, bool fromHex)
        {
            updating = true;
            preview.Background = new SolidColorBrush(c);
            if (!fromHex) hex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            sliders[0].Value = c.R; sliders[1].Value = c.G; sliders[2].Value = c.B;
            updating = false;
        }
        void Commit(Color c) { color = c; EditorColors.Set(id, c); changed(); }
        panel.Children.Add(preview);
        panel.Children.Add(Label("EditorColorHex"));
        panel.Children.Add(hex);
        foreach (var key in new[] { "EditorColorRed", "EditorColorGreen", "EditorColorBlue" })
        {
            panel.Children.Add(Label(key));
            var slider = new Slider { Name = key.Replace("EditorColor", "EditorColorSlider"), Minimum = 0, Maximum = 255, SmallChange = 1, LargeChange = 16, IsSnapToTickEnabled = true, TickFrequency = 1 };
            AutomationProperties.SetName(slider, Loc.T(key));
            sliders.Add(slider);
            panel.Children.Add(slider);
        }
        Show(color, false);
        foreach (var slider in sliders)
            slider.ValueChanged += (_, _) =>
            {
                if (updating) return;
                var c = Color.FromArgb(color.A, (byte)sliders[0].Value, (byte)sliders[1].Value, (byte)sliders[2].Value);
                Show(c, false); Commit(c);
            };
        hex.TextChanged += (_, _) =>
        {
            if (updating || hex.Text is not { } t || !t.StartsWith('#') || t.Length is not (7 or 9) || !Color.TryParse(t, out var c)) return;
            Show(c, true); Commit(c);
        };
        return panel;
    }
}
