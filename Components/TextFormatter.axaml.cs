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
        UpdateHtmlButtons();
        EditorSettingsButton.Flyout = EditorFontsFlyout.Create();
        PreviewSettingsButton.Flyout = PreviewSettingsFlyout.Create();
        // The tooltip names the live (possibly customized) toggle-preview shortcut.
        AttachedToVisualTree += (_, _) => { Mdhub.Services.EditorBehavior.Changed += UpdateHtmlButtons; Mdhub.Localization.Loc.Changed += UpdateLanguage; UpdateHtmlButtons(); Mdhub.Services.Shortcuts.Changed += UpdateLanguage; UpdateLanguage(); };
        DetachedFromVisualTree += (_, _) => { Mdhub.Services.EditorBehavior.Changed -= UpdateHtmlButtons; Mdhub.Localization.Loc.Changed -= UpdateLanguage; Mdhub.Services.Shortcuts.Changed -= UpdateLanguage; };
    }
    private void UpdateLanguage()
    {
        BuildHtmlButtons();
        // Item 50: the link icon's tooltip names its (live) shortcut, e.g. "Insert link (Ctrl+L)".
        ToolTip.SetTip(LinkButton, $"{Mdhub.Localization.Loc.T("InsertLink")} ({Mdhub.Services.Shortcuts.Format("editor.link", ShortcutKeys.IsMac)})");
        ToolTip.SetTip(TogglePreviewButton, Mdhub.Localization.Loc.F("TogglePreviewTip", Mdhub.Services.Shortcuts.Format("view.togglePreview", ShortcutKeys.IsMac)));
    }
    private void UpdateHtmlButtons() => HtmlFormatBar.IsVisible = Mdhub.Services.EditorBehavior.Current.HtmlFormatting;
    public void SetFormattingEnabled(bool enabled, bool markdown = true)
    {
        FormatBar.IsEnabled = enabled;
        TogglePreviewButton.IsEnabled = enabled;
        ExportHtmlButton.IsEnabled = enabled && markdown;
        ExportPdfButton.IsEnabled = enabled && markdown;
    }
    // Item 60: the editor gear is for any text tab, the preview gear for Markdown and HTML; both hide for image, binary and other tabs.
    public void SetSettingsVisibility(bool text, bool hasPreview)
    {
        EditorSettingsButton.IsVisible = text;
        PreviewSettingsButton.IsVisible = text && hasPreview;
    }
    // Item 48: on = the preview pane is showing.
    public bool PreviewOn => TogglePreviewButton.Classes.Contains("on");
    public void SetPreviewOn(bool on) => TogglePreviewButton.Classes.Set("on", on);
    private void TogglePreview_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.TogglePreview);
    private void ExportHtml_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.ExportHtml);
    private void ExportPdf_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.ExportPdf);
    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string action }) EditRequested?.Invoke(action);
    }
    private void Format_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string action }) FormatRequested?.Invoke(action);
    }
}
