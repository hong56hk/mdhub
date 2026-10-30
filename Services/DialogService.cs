using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mdhub.Localization;

namespace Mdhub.Services;

public static class DialogService
{
    public static Task<string?> PromptNameAsync(Window owner, string title, string directory, string initialName)
    {
        var dialog = new Window { Title = title, Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var input = new TextBox { Name = "ItemName", Text = initialName, PlaceholderText = Loc.T("DialogName") };
        var error = new TextBlock { Foreground = ThemeService.Brush("DialogErrorForeground"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var create = new Button { Content = Loc.T("Create"), IsDefault = true, MinWidth = 80, Classes = { "textAction" } };
        var cancel = new Button { Content = Loc.T("Cancel"), IsCancel = true, MinWidth = 80, Classes = { "textAction" } };
        create.Click += (_, _) =>
        {
            var name = input.Text ?? "";
            var message = FolderService.ValidateName(name);
            if (message is null && (File.Exists(Path.Combine(directory, name)) || Directory.Exists(Path.Combine(directory, name))))
                message = Loc.T("NameExists");
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
                new TextBlock { Text = Loc.F("CreateIn", directory), TextWrapping = TextWrapping.Wrap }, input, error,
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
        var dialog = new Window { Title = Loc.T("GoToLineTitle"), Width = 300, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var input = new TextBox { Name = "LineNumber", Text = currentLine.ToString(), PlaceholderText = Loc.T("LineNumber") };
        input.AddHandler(Avalonia.Input.InputElement.TextInputEvent, (_, e) => { if (e.Text is { } t && !t.All(char.IsAsciiDigit)) e.Handled = true; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        input.TextChanged += (_, _) => { if (input.Text is { } t && !t.All(char.IsAsciiDigit)) input.Text = new string(t.Where(char.IsAsciiDigit).ToArray()); };
        var go = new Button { Content = Loc.T("Go"), IsDefault = true, MinWidth = 70, Classes = { "textAction" } };
        var cancel = new Button { Content = Loc.T("Cancel"), IsCancel = true, MinWidth = 70, Classes = { "textAction" } };
        go.Click += (_, _) => { if (Mdhub.Text.EditorCommands.ParseLine(input.Text, lineCount) is { } line) dialog.Close(line); else input.Focus(); };
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 10,
            Children =
            {
                new TextBlock { Name = "LineHint", Text = Loc.F("LineHint", lineCount), Foreground = ThemeService.Brush("DialogHintForeground") }, input,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { go, cancel } }
            }
        };
        ApplyOwnerZoom(dialog, owner);
        return dialog.ShowDialog<int?>(owner);
    }

    // Every shortcut, grouped by area; mdhub's own entries can be rebound (Components/ShortcutsDialog.cs).
    public static Task ShowShortcutsAsync(Window owner) => Mdhub.Components.ShortcutsDialog.ShowAsync(owner);

    public static Task<string?> ShowAsync(Window owner, string title, string message, params string[] choices)
    {
        var dialog = new Window { Title = title, Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var choice in choices)
        {
            var button = new Button { Content = Loc.T(choice), MinWidth = 80, Classes = { "textAction" } };
            button.Click += (_, _) => dialog.Close(choice);
            buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 20,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, buttons } };
        ApplyOwnerZoom(dialog, owner);
        return dialog.ShowDialog<string?>(owner);
    }
}
