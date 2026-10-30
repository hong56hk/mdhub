using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

// Item 58: About > "MIT License" opens the bundled LICENSE in a read-only, pathless tab (never saved or restored).
public partial class MainView
{
    internal NoteDocument OpenLicenseTab()
    {
        var existing = _documents.FirstOrDefault(d => d.Path is null && d.IsReadOnlyText && d.UntitledName == AppInfo.LicenseTabName);
        if (existing is not null) { FileTabs.SelectedDocument = existing; return existing; }
        var text = AppInfo.LicenseText();
        var document = new NoteDocument { SavedText = text, UntitledName = AppInfo.LicenseTabName, IsReadOnlyText = true };
        InitializeTextDocument(document, text);
        _documents.Add(document);
        FileTabs.SelectedDocument = document;
        return document;
    }
}
