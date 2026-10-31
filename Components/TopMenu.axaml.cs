using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Avalonia.Media;
using Mdhub.Localization;

namespace Mdhub.Components;

public partial class TopMenu : UserControl
{
    public event Action<WorkspaceAction>? ActionRequested;
    public event Action<string>? EditRequested;
    public event Action<WindowAction>? WindowActionRequested;
    public event Action<string>? CommandRequested;
    // (path, isFolder) for an Open Recent entry.
    public event Action<string, bool>? RecentRequested;
    private void Command_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && ShortcutKeys.GetId(item) is { } id) CommandRequested?.Invoke(id);
    }
    public TopMenu()
    {
        InitializeComponent();
        TitleLogo.Source = Services.AppIcon.Logo(); // item 69
        ShortcutKeys.ApplyGestures(MainMenu.Items.OfType<MenuItem>());
        UpdateThemeChecks(Services.ThemeService.Current);
        AttachedToVisualTree += (_, _) => { Services.ThemeService.Changed += UpdateThemeChecks; UpdateThemeChecks(Services.ThemeService.Current); };
        DetachedFromVisualTree += (_, _) => Services.ThemeService.Changed -= UpdateThemeChecks;
        RebuildRecent();
        AttachedToVisualTree += (_, _) => { Services.RecentService.Changed += RebuildRecent; RebuildRecent(); };
        DetachedFromVisualTree += (_, _) => Services.RecentService.Changed -= RebuildRecent;
        // Each language is listed under its own name, whatever the current language.
        foreach (var language in Loc.Languages)
        {
            var item = new MenuItem { Header = language.NativeName, Tag = language.Code, ToggleType = MenuItemToggleType.Radio, GroupName = "language" };
            AutomationProperties.SetName(item, language.NativeName);
            item.Click += (_, _) => LocResources.Choose(language.Code);
            LanguageMenu.Items.Add(item);
        }
        UpdateLanguage();
        AttachedToVisualTree += (_, _) => { Loc.Changed += UpdateLanguage; UpdateLanguage(); };
        DetachedFromVisualTree += (_, _) => Loc.Changed -= UpdateLanguage;
    }
    private WindowState _windowState;
    private void UpdateLanguage()
    {
        foreach (var item in LanguageMenu.Items.OfType<MenuItem>()) item.IsChecked = Equals(item.Tag, Loc.Current);
        UpdateWindowState(_windowState);
        RebuildRecent();
    }
    private void RebuildRecent()
    {
        var items = new List<Control>();
        MenuItem Entry(string path, bool folder)
        {
            var name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) name = path;
            var header = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            header.Children.Add(new TextBlock { Text = name });
            var parent = new TextBlock { Text = Services.RecentList.ShortParent(path) };
            parent.Classes.Add("recentPath");
            header.Children.Add(parent);
            var item = new MenuItem { Header = header, Tag = path };
            ToolTip.SetTip(item, path);
            AutomationProperties.SetName(item, name);
            item.Click += (_, _) => RecentRequested?.Invoke(path, folder);
            return item;
        }
        items.AddRange(Services.RecentService.Files.Select(p => Entry(p, false)));
        if (Services.RecentService.Files.Count > 0 && Services.RecentService.Folders.Count > 0) items.Add(new Separator());
        items.AddRange(Services.RecentService.Folders.Select(p => Entry(p, true)));
        if (items.Count > 0)
        {
            items.Add(new Separator());
            var clear = new MenuItem { Header = Loc.T("RecentClear") };
            clear.Click += (_, _) => Services.RecentService.Clear();
            items.Add(clear);
        }
        OpenRecentMenu.Items.Clear();
        foreach (var item in items) OpenRecentMenu.Items.Add(item);
        OpenRecentMenu.IsEnabled = items.Count > 0;
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
        _windowState = state;
        var maximized = state == WindowState.Maximized;
        var label = Loc.T(maximized ? "Restore" : "Maximize");
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
    private void ExportHtml_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.ExportHtml);
    private void ExportPdf_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.ExportPdf);
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
