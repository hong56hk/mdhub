using System.Diagnostics;
using Avalonia.Platform.Storage;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

// Item 51: File > Export > HTML / PDF and the format-bar export buttons.
public partial class MainView
{
    // Test hooks: (suggested full path, extension) -> chosen path or null; and the "Open" action.
    internal Func<string, string, Task<string?>>? ExportPickerOverride { get; set; }
    internal static Action<string>? OpenFileOverride { get; set; }

    private async Task ExportAsync(bool pdf)
    {
        if (_active is not { IsMarkdown: true } document) { UpdateStatus(Loc.T("ExportMarkdownOnly")); return; }
        var extension = pdf ? "pdf" : "html";
        var folder = document.Path is { } p ? Path.GetDirectoryName(p) : FolderExplorer.OpenedFolder;
        var name = Path.GetFileNameWithoutExtension(document.Path ?? document.UntitledName);
        string? target = null;
        await RunAsync(async () =>
        {
            var suggested = Path.Combine(folder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"{name}.{extension}");
            var path = ExportPickerOverride is not null ? await ExportPickerOverride(suggested, extension) : await PickExportPathAsync(suggested, extension, pdf);
            if (path is null) return;
            var html = MarkdownExporter.ToHtml(document.Text, name, document.Path is null ? null : Path.GetDirectoryName(document.Path));
            if (pdf)
            {
                UpdateStatus(Loc.T("ExportingPdf"));
                await MarkdownExporter.ToPdfAsync(html, path);
            }
            else await File.WriteAllTextAsync(path, html, new System.Text.UTF8Encoding(false));
            target = path;
        }, focusEditor: false);
        if (target is null) return;
        UpdateStatus(Loc.F("Exported", target));
        var opened = target;
        BottomStatusBar.SetAction(Loc.T("ExportOpen"), () => OpenExported(opened));
        if (FolderExplorer.Contains(target)) await FolderExplorer.RefreshAsync();
    }

    private async Task<string?> PickExportPathAsync(string suggested, string extension, bool pdf)
    {
        IStorageFolder? start = null;
        if (Path.GetDirectoryName(suggested) is { } dir && Directory.Exists(dir)) start = await Host.StorageProvider.TryGetFolderFromPathAsync(dir);
        using var file = await Host.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T(pdf ? "ExportPdfTip" : "ExportHtmlTip"), SuggestedFileName = Path.GetFileName(suggested), SuggestedStartLocation = start,
            DefaultExtension = extension, ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(Loc.T(pdf ? "PdfFiles" : "HtmlFiles")) { Patterns = pdf ? ["*.pdf"] : ["*.html", "*.htm"] }],
        });
        if (file is null) return null;
        return file.TryGetLocalPath() ?? throw new IOException(Loc.T("ChooseLocalFile"));
    }

    private void OpenExported(string path)
    {
        if (OpenFileOverride is not null) { OpenFileOverride(path); return; }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { UpdateStatus(ex.Message); }
    }
}