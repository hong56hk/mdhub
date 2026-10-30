using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Mdhub.Components;

public partial class TextFormatter : UserControl
{
    public event Action<WorkspaceAction>? ActionRequested;
    public event Action<string>? EditRequested;
    public event Action<string>? FormatRequested;
    public TextFormatter()
    {
        InitializeComponent();
        UpdateLanguage();
        // The tooltip names the live (possibly customized) toggle-preview shortcut.
        AttachedToVisualTree += (_, _) => { Mdhub.Localization.Loc.Changed += UpdateLanguage; Mdhub.Services.Shortcuts.Changed += UpdateLanguage; UpdateLanguage(); };
        DetachedFromVisualTree += (_, _) => { Mdhub.Localization.Loc.Changed -= UpdateLanguage; Mdhub.Services.Shortcuts.Changed -= UpdateLanguage; };
    }
    private void UpdateLanguage()
    {
        // Item 50: the link icon's tooltip names its (live) shortcut, e.g. "Insert link (Ctrl+L)".
        ToolTip.SetTip(LinkButton, $"{Mdhub.Localization.Loc.T("InsertLink")} ({Mdhub.Services.Shortcuts.Format("editor.link", ShortcutKeys.IsMac)})");
        ToolTip.SetTip(TogglePreviewButton, Mdhub.Localization.Loc.F("TogglePreviewTip", Mdhub.Services.Shortcuts.Format("view.togglePreview", ShortcutKeys.IsMac)));
    }
    public void SetFormattingEnabled(bool enabled)
    {
        FormatBar.IsEnabled = enabled;
        TogglePreviewButton.IsEnabled = enabled;
    }
    // Item 48: on = the preview pane is showing.
    public bool PreviewOn => TogglePreviewButton.Classes.Contains("on");
    public void SetPreviewOn(bool on) => TogglePreviewButton.Classes.Set("on", on);
    private void TogglePreview_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.TogglePreview);
    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string action }) EditRequested?.Invoke(action);
    }
    private void Format_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string action }) FormatRequested?.Invoke(action);
    }
}
