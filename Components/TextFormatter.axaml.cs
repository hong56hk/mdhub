using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Mdhub.Components;

public partial class TextFormatter : UserControl
{
    public event Action<WorkspaceAction>? ActionRequested;
    public event Action<string>? EditRequested;
    public event Action<string>? FormatRequested;
    public TextFormatter() => InitializeComponent();
    public void SetFormattingEnabled(bool enabled) => FormatBar.IsEnabled = enabled;
    private void OpenFolder_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.OpenFolder);
    private void Save_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.Save);
    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string action }) EditRequested?.Invoke(action);
    }
    private void Format_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string action }) FormatRequested?.Invoke(action);
    }
}
