using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Mdhub.Localization;
using Mdhub.Services;

namespace Mdhub.Components;

// Settings > Shortcuts: every shortcut grouped by area. mdhub's own entries can be rebound: click the row or its
// edit icon, press the new combination (Esc cancels). A clash shows an inline warning with Reassign / Cancel;
// customized rows carry a dot and a reset icon; "Reset all shortcuts" asks first. Changes save right away.
internal static class ShortcutsDialog
{
    private const string EditIcon = "M 3,13 L 3,10.5 10.5,3 13,5.5 5.5,13 Z M 9,4.5 L 11.5,7";
    private const string ResetIcon = "M 4,8 A 4,4 0 1 0 5.2,5.2 M 4,3 L 4,6 7,6";

    private sealed class Row(Shortcut shortcut, Grid grid, TextBlock keys, TextBlock mark, Button? edit, Button? reset)
    {
        public Shortcut Shortcut { get; } = shortcut;
        public Grid Grid { get; } = grid;
        public TextBlock Keys { get; } = keys;
        public TextBlock Mark { get; } = mark;
        public Button? Edit { get; } = edit;
        public Button? Reset { get; } = reset;
    }

    private static Button IconButton(string name, string data, string tip)
    {
        var button = new Button { Name = name, Classes = { "findAction" }, Focusable = false, Margin = new Thickness(4, 0, 0, 0) };
        var path = new Avalonia.Controls.Shapes.Path { Classes = { "findIcon" }, Data = Geometry.Parse(data) };
        path.Bind(Shape.StrokeProperty, button.GetObservable(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty));
        button.Content = path;
        ToolTip.SetTip(button, tip);
        AutomationPropertiesName(button, tip);
        return button;
    }

    private static void AutomationPropertiesName(Control c, string name) => Avalonia.Automation.AutomationProperties.SetName(c, name);

    public static Task ShowAsync(Window owner)
    {
        var mac = ShortcutKeys.IsMac;
        var dialog = new Window { Title = Loc.T("ShortcutsTitle"), Width = 560, Height = 600, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var list = new StackPanel { Name = "ShortcutList", Spacing = 2, Margin = new Thickness(20, 8, 20, 16) };
        var rows = new List<Row>();
        string? recording = null;
        var resetAll = new Button { Name = "ResetAllShortcuts", Content = Loc.T("ShortcutResetAll"), Classes = { "textAction" }, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 16, MinHeight = 42, MinWidth = 80 };
        ShortcutGesture? pending = null;

        var conflictText = new TextBlock { Name = "ShortcutConflictText", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var reassign = new Button { Name = "ShortcutReassign", Content = Loc.T("ShortcutReassign"), Classes = { "textAction" }, MinWidth = 80 };
        var keepCancel = new Button { Name = "ShortcutConflictCancel", Content = Loc.T("Cancel"), Classes = { "textAction" }, MinWidth = 80 };
        var conflictButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { reassign, keepCancel } };
        var conflict = new Border
        {
            Name = "ShortcutConflict", IsVisible = false, Margin = new Thickness(20, 12, 20, 0), Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1), BorderBrush = ThemeService.Brush("Danger"), Background = ThemeService.Brush("AccentSoft"),
            Child = new StackPanel { Spacing = 8, Children = { conflictText, conflictButtons } }
        };

        void Refresh()
        {
            foreach (var row in rows)
            {
                var s = row.Shortcut;
                var isRecording = recording == s.Id;
                var text = s.Format(mac);
                row.Keys.Text = isRecording ? Loc.T("ShortcutRecording") : text.Length == 0 ? Loc.T("ShortcutUnassigned") : text;
                row.Keys.FontStyle = isRecording || text.Length == 0 ? FontStyle.Italic : FontStyle.Normal;
                row.Mark.IsVisible = s.IsCustomized;
                if (row.Reset is { } reset) reset.IsVisible = s.IsCustomized;
                row.Grid.Classes.Set("customized", s.IsCustomized);
                row.Grid.Classes.Set("recording", isRecording);
                row.Grid.Background = isRecording ? ThemeService.Brush("AccentSoft") : Brushes.Transparent;
            }
            resetAll.IsEnabled = Shortcuts.AnyCustomized;
        }

        void Changed() { Refresh(); SessionManager.SaveSettings(); }

        void StopRecording() { recording = null; pending = null; conflict.IsVisible = false; Refresh(); }

        // The row takes keyboard focus so the key presses that follow reach this dialog's recorder.
        void StartRecording(string id) { recording = id; pending = null; conflict.IsVisible = false; Refresh(); rows.FirstOrDefault(r => r.Shortcut.Id == id)?.Grid.Focus(); }

        void Record(ShortcutGesture gesture)
        {
            if (recording is not { } id) return;
            var conflicts = Shortcuts.FindConflicts(id, gesture);
            if (conflicts.Count == 0) { Shortcuts.Rebind(id, gesture); StopRecording(); Changed(); return; }
            pending = gesture;
            var other = conflicts[0];
            var builtIn = conflicts.Any(c => !c.IsCustomizable);
            conflictText.Text = Loc.F(builtIn ? "ShortcutConflictBuiltIn" : "ShortcutConflict", gesture.Format(mac), (builtIn ? conflicts.First(c => !c.IsCustomizable) : other).DisplayLabel);
            reassign.IsVisible = !builtIn;
            conflict.IsVisible = true;
        }

        reassign.Click += (_, _) =>
        {
            if (recording is { } id && pending is { } gesture && Shortcuts.Rebind(id, gesture, reassign: true)) { StopRecording(); Changed(); }
        };
        keepCancel.Click += (_, _) => StopRecording();

        // Areas in enum order (File, Edit, View, Find, Zoom, Editor), not first-appearance order in the table.
        foreach (var group in Shortcuts.All.GroupBy(s => s.Area).OrderBy(g => g.Key))
        {
            list.Children.Add(new TextBlock { Text = Loc.T("Area" + group.Key), FontWeight = FontWeight.SemiBold, FontSize = 15, Margin = new Thickness(0, 10, 0, 2), Classes = { "shortcut-area" } });
            foreach (var s in group)
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), Classes = { "shortcut-row" }, Tag = s.Id, MinHeight = 26 };
                var label = new TextBlock { Text = s.DisplayLabel, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                var mark = new TextBlock { Name = "ShortcutCustomMark", Text = "\u25CF", FontSize = 9, Foreground = ThemeService.Brush("Accent"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                ToolTip.SetTip(mark, Loc.T("ShortcutCustomized"));
                var keys = new TextBlock { Classes = { "shortcut-keys" }, Foreground = ThemeService.Brush("ShortcutKeyForeground"), FontFamily = new FontFamily("Consolas, Menlo, monospace"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(mark, 1); Grid.SetColumn(keys, 2);
                grid.Children.Add(label); grid.Children.Add(mark); grid.Children.Add(keys);
                Button? edit = null, reset = null;
                if (s.IsCustomizable)
                {
                    edit = IconButton("ShortcutEdit", EditIcon, Loc.T("ShortcutEdit"));
                    reset = IconButton("ShortcutReset", ResetIcon, Loc.T("ShortcutReset"));
                    var id = s.Id;
                    edit.Click += (_, _) => StartRecording(id);
                    reset.Click += (_, _) => { if (recording == id) StopRecording(); Shortcuts.Reset(id); Changed(); };
                    grid.Cursor = new Cursor(StandardCursorType.Hand);
                    grid.Focusable = true;
                    grid.PointerPressed += (_, e) => { if (e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed) { StartRecording(id); e.Handled = true; } };
                    Grid.SetColumn(edit, 3); Grid.SetColumn(reset, 4);
                    grid.Children.Add(edit); grid.Children.Add(reset);
                }
                else
                {
                    // Built into the text box: listed for reference only, with the icon column kept so the keys align.
                    var spacer = new Border { Width = 28 };
                    Grid.SetColumn(spacer, 3);
                    grid.Children.Add(spacer);
                    ToolTip.SetTip(grid, Loc.T("ShortcutBuiltIn"));
                }
                list.Children.Add(grid);
                rows.Add(new Row(s, grid, keys, mark, edit, reset));
            }
        }

        // While recording, every key goes to the recorder (Esc cancels); otherwise Esc/Enter work as usual.
        dialog.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (recording is null) return;
            e.Handled = true;
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) { StopRecording(); return; }
            if (conflict.IsVisible) return;
            if (ShortcutKeys.FromKey(e.Key, e.KeyModifiers) is { } gesture) Record(gesture);
        }, RoutingStrategies.Tunnel);

        var close = new Button {
            Name = "ShortcutsClose",
            Content = Loc.T("Close"),
            IsDefault = true,
            IsCancel = true,
            FontSize = 16,
            MinWidth = 115,
            MinHeight = 42,
            Classes = { "textAction" },
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        close.Click += (_, _) => dialog.Close();
        resetAll.Click += async (_, _) =>
        {
            StopRecording();
            if (await DialogService.ShowAsync(dialog, Loc.T("ShortcutResetAll"), Loc.T("ShortcutResetAllConfirm"), "ResetAll", "Cancel") == "ResetAll")
            {
                Shortcuts.ResetAll();
                Changed();
            }
        };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(20, 8, 20, 16), Children = { resetAll, close } };
        Grid.SetColumn(close, 1);
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(conflict, Dock.Top);
        root.Children.Add(footer);
        root.Children.Add(conflict);
        root.Children.Add(new ScrollViewer { Content = list });
        dialog.Content = root;
        Refresh();
        Shortcuts.Changed += Refresh;
        dialog.Closed += (_, _) => Shortcuts.Changed -= Refresh;
        DialogService.ApplyOwnerZoom(dialog, owner);
        ThemeService.PrepareTitleBar(dialog);
        return dialog.ShowDialog(owner);
    }
}
