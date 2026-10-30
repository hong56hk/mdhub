using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Avalonia.Media;

namespace Mdhub.Components;

public partial class TopMenu : UserControl
{
    public event Action<WorkspaceAction>? ActionRequested;
    public event Action<string>? EditRequested;
    public event Action<WindowAction>? WindowActionRequested;
    public event Action<string>? CommandRequested;
    private void Command_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && ShortcutKeys.GetId(item) is { } id) CommandRequested?.Invoke(id);
    }
    public TopMenu()
    {
        InitializeComponent();
        ShortcutKeys.ApplyGestures(MainMenu.Items.OfType<MenuItem>());
        UpdateThemeChecks(Services.ThemeService.Current);
        AttachedToVisualTree += (_, _) => { Services.ThemeService.Changed += UpdateThemeChecks; UpdateThemeChecks(Services.ThemeService.Current); };
        DetachedFromVisualTree += (_, _) => Services.ThemeService.Changed -= UpdateThemeChecks;
    }
    private void UpdateThemeChecks(Services.AppTheme theme)
    {
        ThemeDarkMenuItem.IsChecked = theme == Services.AppTheme.Dark;
        ThemeLightMenuItem.IsChecked = theme == Services.AppTheme.Light;
    }
    private void Theme_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string name }) Services.ThemeService.Apply(Services.ThemeService.Parse(name));
    }
    private void Shortcuts_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.Shortcuts);
    public void UpdateWindowState(WindowState state)
    {
        var maximized = state == WindowState.Maximized;
        var label = maximized ? "Restore" : "Maximize";
        ToolTip.SetTip(MaximizeButton, label);
        AutomationProperties.SetName(MaximizeButton, label);
        MaximizeIcon.Data = Geometry.Parse(maximized
            ? "M 2.5,2.5 L 2.5,0.5 9.5,0.5 9.5,7.5 7.5,7.5 M 0.5,2.5 L 7.5,2.5 7.5,9.5 0.5,9.5 Z"
            : "M 0.5,0.5 L 9.5,0.5 9.5,9.5 0.5,9.5 Z");
    }
    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowActionRequested?.Invoke(WindowAction.Minimize);
    private void Maximize_Click(object? sender, RoutedEventArgs e) => WindowActionRequested?.Invoke(WindowAction.ToggleMaximize);
    private void CloseWindow_Click(object? sender, RoutedEventArgs e) => WindowActionRequested?.Invoke(WindowAction.Close);
    private void New_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.New);
    private void Open_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.Open);
    private void OpenFolder_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.OpenFolder);
    private void Save_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.Save);
    private void SaveAs_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.SaveAs);
    private void CloseTab_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.CloseTab);
    private void Exit_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.Exit);
    private void ToggleFolders_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.ToggleFolders);
    private void TogglePreview_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.TogglePreview);
    private void WordWrap_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.WordWrap);
    private void RefreshFolder_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.RefreshFolder);
    private void Guide_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.Guide);
    private void About_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.About);
    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string action }) EditRequested?.Invoke(action);
    }
}
