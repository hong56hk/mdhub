using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mdhub.Localization;

namespace Mdhub.Services;

public static class DialogService
{
    // displayDirectory: how "Create in:" names the target (item 42: relative to the open folder); the tooltip has the full path.
    public static Task<string?> PromptNameAsync(Window owner, string title, string directory, string initialName, string? displayDirectory = null)
    {
        var dialog = new Window { Title = title, Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var input = new TextBox { Name = "ItemName", Text = initialName, PlaceholderText = Loc.T("DialogName") };
        var error = new TextBlock { Foreground = ThemeService.Brush("DialogErrorForeground"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var create = new Button { Content = Loc.T("Create"), IsDefault = true, MinWidth = 80, MinHeight = 42, FontSize = 16, Classes = { "textAction" }, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = Loc.T("Cancel"), IsCancel = true, MinWidth = 80, MinHeight = 42, FontSize = 16, Classes = { "textAction" }, HorizontalContentAlignment = HorizontalAlignment.Center };
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
        // As in rename: a file name pre-selects only its stem ("Untitled" of "Untitled.md"); a folder name is selected whole.
        dialog.Opened += (_, _) =>
        {
            input.Focus();
            var stem = Path.GetFileNameWithoutExtension(initialName);
            if (Path.HasExtension(initialName) && stem.Length > 0) { input.SelectionStart = 0; input.SelectionEnd = stem.Length; }
            else input.SelectAll();
        };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 12,
            Children =
            {
                CreateInText(directory, displayDirectory), input, error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { create, cancel } }
            }
        };
        ApplyOwnerZoom(dialog, owner);
        ThemeService.PrepareTitleBar(dialog);
        return dialog.ShowDialog<string?>(owner);
    }

    private static TextBlock CreateInText(string directory, string? displayDirectory)
    {
        var text = new TextBlock { Name = "CreateInText", Text = Loc.F("CreateIn", displayDirectory ?? directory), TextWrapping = TextWrapping.Wrap };
        ToolTip.SetTip(text, directory);
        return text;
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
        ThemeService.PrepareTitleBar(dialog);
        return dialog.ShowDialog<int?>(owner);
    }

    // Every shortcut, grouped by area; mdhub's own entries can be rebound (Components/ShortcutsDialog.cs).
    public static Task ShowShortcutsAsync(Window owner) => Mdhub.Components.ShortcutsDialog.ShowAsync(owner);

    // Item 34: About shows the app logo beside the text.
    public static Task<string?> ShowAboutAsync(Window owner) => ShowAsync(owner, Loc.T("AboutTitle"), Loc.T("AboutText"),
        new Avalonia.Controls.Image { Name = "AboutLogo", Source = AppIcon.Logo(), Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Top }, "OK");

    public static Task<string?> ShowAsync(Window owner, string title, string message, params string[] choices) => ShowAsync(owner, title, message, null, choices);

    private static Task<string?> ShowAsync(Window owner, string title, string message, Control? logo, params string[] choices)
    {
        var dialog = new Window { Title = title, Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var choice in choices)
        {
            var button = new Button { Content = Loc.T(choice), MinWidth = 80, Classes = { "textAction" }, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 42, FontSize = 16 };
            button.Click += (_, _) => dialog.Close(choice);
            buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 20,
            Children = { logo is null ? new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }
                : new DockPanel { Children = { logo, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 0, 0, 0) } } }, buttons } };
        ApplyOwnerZoom(dialog, owner);
        ThemeService.PrepareTitleBar(dialog);
        return dialog.ShowDialog<string?>(owner);
    }
}
