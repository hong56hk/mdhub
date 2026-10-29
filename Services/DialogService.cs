using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Mdhub.Services;

public static class DialogService
{
    public static Task<string?> PromptNameAsync(Window owner, string title, string directory, string initialName, string confirmText = "Create")
    {
        var dialog = new Window { Title = title, Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var input = new TextBox { Name = "ItemName", Text = initialName, PlaceholderText = "Name" };
        var error = new TextBlock { Foreground = Brush.Parse("#F29D9D"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var create = new Button { Content = confirmText, IsDefault = true, MinWidth = 80 };
        var renaming = confirmText != "Create";
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        create.Click += (_, _) =>
        {
            var name = input.Text ?? "";
            var message = FolderService.ValidateName(name);
            if (renaming && message is null && name == initialName) { dialog.Close(null); return; }
            var caseOnly = renaming && string.Equals(name, initialName, StringComparison.OrdinalIgnoreCase);
            if (message is null && !caseOnly && (File.Exists(Path.Combine(directory, name)) || Directory.Exists(Path.Combine(directory, name))))
                message = "A file or folder with that name already exists.";
            if (message is not null) { error.Text = message; error.IsVisible = true; input.Focus(); return; }
            dialog.Close(name);
        };
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 12,
            Children =
            {
                new TextBlock { Text = $"{(renaming ? "Rename in" : "Create in")}: {directory}", TextWrapping = TextWrapping.Wrap }, input, error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { create, cancel } }
            }
        };
        ApplyOwnerZoom(dialog, owner);
        return dialog.ShowDialog<string?>(owner);
    }

    // Dialogs are separate windows, so the owner's zoom transform does not reach them: scale content and width here.
    internal static void ApplyOwnerZoom(Window dialog, Window owner)
    {
        if (owner is not MainWindow main || main.ZoomPercent == ZoomLevel.Default || dialog.Content is not Control content) return;
        var scale = main.ZoomScale;
        dialog.Content = null;
        dialog.Content = new LayoutTransformControl { Name = "DialogZoom", Child = content, LayoutTransform = new Avalonia.Media.ScaleTransform(scale, scale) };
        if (!double.IsNaN(dialog.Width)) dialog.Width *= scale;
        if (!double.IsNaN(dialog.Height) && dialog.SizeToContent is SizeToContent.Manual or SizeToContent.Width) dialog.Height *= scale;
    }

    // Small "Go to line" prompt: digits only, returns the clamped 1-based line or null on cancel.
    public static Task<int?> PromptLineAsync(Window owner, int lineCount, int currentLine)
    {
        var dialog = new Window { Title = "Go to line", Width = 300, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var input = new TextBox { Name = "LineNumber", Text = currentLine.ToString(), PlaceholderText = "Line number" };
        input.AddHandler(Avalonia.Input.InputElement.TextInputEvent, (_, e) => { if (e.Text is { } t && !t.All(char.IsAsciiDigit)) e.Handled = true; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        input.TextChanged += (_, _) => { if (input.Text is { } t && !t.All(char.IsAsciiDigit)) input.Text = new string(t.Where(char.IsAsciiDigit).ToArray()); };
        var go = new Button { Content = "Go", IsDefault = true, MinWidth = 70 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 70 };
        go.Click += (_, _) => { if (Mdhub.Text.EditorCommands.ParseLine(input.Text, lineCount) is { } line) dialog.Close(line); else input.Focus(); };
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 10,
            Children =
            {
                new TextBlock { Name = "LineHint", Text = $"Line number (1 to {lineCount})", Foreground = Brush.Parse("#A9B1BC") }, input,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { go, cancel } }
            }
        };
        ApplyOwnerZoom(dialog, owner);
        return dialog.ShowDialog<int?>(owner);
    }

    // Read-only list of every shortcut, grouped by area, from the shortcut table.
    public static Task ShowShortcutsAsync(Window owner)
    {
        var mac = OperatingSystem.IsMacOS();
        var dialog = new Window { Title = "Shortcuts", Width = 520, Height = 560, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var list = new StackPanel { Name = "ShortcutList", Spacing = 4, Margin = new Thickness(20, 16) };
        foreach (var group in Shortcuts.All.GroupBy(s => s.Area))
        {
            list.Children.Add(new TextBlock { Text = group.Key.ToString(), FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 15, Margin = new Thickness(0, 10, 0, 2), Classes = { "shortcut-area" } });
            foreach (var s in group)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Classes = { "shortcut-row" }, Tag = s.Id };
                row.Children.Add(new TextBlock { Text = s.Label });
                var keys = new TextBlock { Text = s.Format(mac), Foreground = Avalonia.Media.Brush.Parse("#9AA4B2"), FontFamily = new Avalonia.Media.FontFamily("Consolas, Menlo, monospace") };
                Grid.SetColumn(keys, 1);
                row.Children.Add(keys);
                list.Children.Add(row);
            }
        }
        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true, MinWidth = 80, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(20, 0, 20, 16) };
        close.Click += (_, _) => dialog.Close();
        var root = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = list });
        dialog.Content = root;
        ApplyOwnerZoom(dialog, owner);
        return dialog.ShowDialog(owner);
    }

    public static Task<string?> ShowAsync(Window owner, string title, string message, params string[] choices)
    {
        var dialog = new Window { Title = title, Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var choice in choices)
        {
            var button = new Button { Content = choice, MinWidth = 80 };
            button.Click += (_, _) => dialog.Close(choice);
            buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 20,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, buttons } };
        ApplyOwnerZoom(dialog, owner);
        return dialog.ShowDialog<string?>(owner);
    }
}
