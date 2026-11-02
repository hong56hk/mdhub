using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Mdhub.Components;

public partial class TextFormatter : UserControl
{
    public event Action<WorkspaceAction>? ActionRequested;
    public event Action<string>? EditRequested;
    public event Action<string>? FormatRequested;
    public void UpdateEmoji(Mdhub.Models.NoteDocument document)
    {
        EmojiButton.IsEnabled = !document.IsViewOnly && !document.IsReadOnlyText && !document.Editor.IsReadOnly && Mdhub.Services.TextEncodings.SupportsEmoji(document.Encoding);
        ToolTip.SetTip(EmojiButton, Mdhub.Localization.Loc.T(Mdhub.Services.TextEncodings.SupportsEmoji(document.Encoding) ? "EmojiInput" : "EmojiUnsupported"));
    }

    public event Action<Control>? EmojiRequested;
    private void EmojiButton_Click(object? sender, RoutedEventArgs e) => EmojiRequested?.Invoke(EmojiButton);

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
        ToolTip.SetTip(EditorOnlyButton, $"{Mdhub.Localization.Loc.T("ViewModeEditor")} ({Mdhub.Services.Shortcuts.Format("view.togglePreview", ShortcutKeys.IsMac)})");
        ToolTip.SetTip(TogglePreviewButton, Mdhub.Localization.Loc.T("ViewModeSplit"));
        ToolTip.SetTip(SyncButton, $"{Mdhub.Localization.Loc.T("Sc.view.previewCaret")} ({Mdhub.Services.Shortcuts.Format("view.previewCaret", ShortcutKeys.IsMac)})");
        ToolTip.SetTip(ViewerOnlyButton, $"{Mdhub.Localization.Loc.T("ViewModeViewer")} ({Mdhub.Services.Shortcuts.Format("view.toggleEditor", ShortcutKeys.IsMac)})");
    }
    private void UpdateHtmlButtons() => HtmlFormatBar.IsVisible = Mdhub.Services.EditorBehavior.Current.HtmlFormatting;
    public void SetFormattingEnabled(bool enabled, bool markdown = true)
    {
        FormatBar.IsEnabled = enabled;
        EditorOnlyButton.IsEnabled = TogglePreviewButton.IsEnabled = ViewerOnlyButton.IsEnabled = enabled;
        ExportHtmlButton.IsEnabled = enabled && markdown;
        ExportPdfButton.IsEnabled = enabled && markdown;
    }
    // Item 60: the editor gear is for any text tab, the preview gear for Markdown and HTML; both hide for image, binary and other tabs.
    public void SetSettingsVisibility(bool text, bool hasPreview)
    {
        EditorSettingsButton.IsVisible = text;
        PreviewSettingsButton.IsVisible = text && hasPreview;
    }
    public bool PreviewOn => TogglePreviewButton.Classes.Contains("on") || ViewerOnlyButton.Classes.Contains("on");
    public void SetViewMode(int mode)
    {
        EditorOnlyButton.Classes.Set("on", mode == 0);
        TogglePreviewButton.Classes.Set("on", mode == 1);
        ViewerOnlyButton.Classes.Set("on", mode == 2);
    }
    public void SetSyncEnabled(bool enabled) => SyncButton.IsEnabled = enabled;
    private void Sync_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.SyncCaret);
    private void EditorOnly_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.EditorOnly);
    private void SplitView_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.SplitView);
    private void ViewerOnly_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.ViewerOnly);
    public void SetOutlineState(bool available, bool visible)
    {
        OutlineButton.IsEnabled = available;
        OutlineButton.Classes.Set("on", visible);
    }
    private void Outline_Click(object? sender, RoutedEventArgs e) => ActionRequested?.Invoke(WorkspaceAction.ToggleOutline);
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
