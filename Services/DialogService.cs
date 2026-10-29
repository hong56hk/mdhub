using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Mdhub.Services;

public static class DialogService
{
    public static Task<string?> PromptNameAsync(Window owner, string title, string directory, string initialName)
    {
        var dialog = new Window { Title = title, Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var input = new TextBox { Name = "ItemName", Text = initialName, PlaceholderText = "Name" };
        var error = new TextBlock { Foreground = Brush.Parse("#F29D9D"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var create = new Button { Content = "Create", IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        create.Click += (_, _) =>
        {
            var name = input.Text ?? "";
            var message = FolderService.ValidateName(name);
            if (message is null && (File.Exists(Path.Combine(directory, name)) || Directory.Exists(Path.Combine(directory, name))))
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
                new TextBlock { Text = $"Create in: {directory}", TextWrapping = TextWrapping.Wrap }, input, error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { create, cancel } }
            }
        };
        return dialog.ShowDialog<string?>(owner);
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
        return dialog.ShowDialog<string?>(owner);
    }
}
