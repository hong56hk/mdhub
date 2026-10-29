using Avalonia.Controls;
using Mdhub.Models;
using Mdhub.Text;

namespace Mdhub.Components;

public partial class TextEditor : UserControl
{
    private NoteDocument? _document;
    public TextEditor() => InitializeComponent();
    public void ShowDocument(NoteDocument document)
    {
        _document = document;
        EditorHost.Content = document.Editor;
        UpdateFileKind(document);
    }
    public void UpdateFileKind(NoteDocument document) => FileKind.Text = document.IsMarkdown ? "Markdown" : "Plain text";
    public void FocusEditor() => _document?.Editor.Focus();
    public void Edit(string action)
    {
        if (_document is null) return;
        var editor = _document.Editor;
        switch (action)
        {
            case "undo": editor.Undo(); break;
            case "redo": editor.Redo(); break;
            case "cut": editor.Cut(); break;
            case "copy": editor.Copy(); break;
            case "paste": editor.Paste(); break;
            case "select all": editor.SelectAll(); break;
        }
        editor.Focus();
    }
    public void Format(string action)
    {
        if (_document?.IsMarkdown != true) return;
        var editor = _document.Editor;
        var edit = MarkdownFormatting.Apply(_document.Text, editor.SelectionStart, editor.SelectionEnd, action);
        editor.SelectionStart = edit.Start;
        editor.SelectionEnd = edit.End;
        editor.SelectedText = edit.Replacement;
        editor.SelectionStart = edit.Start + edit.SelectionOffset;
        editor.SelectionEnd = editor.SelectionStart + edit.SelectionLength;
        editor.Focus();
    }
}
