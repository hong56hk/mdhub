using Avalonia.Controls;
using Mdhub.Models;
using Mdhub.Text;

namespace Mdhub.Components;

public partial class TextEditor : UserControl
{
    private NoteDocument? _document;
    public TextEditor()
    {
        InitializeComponent();
        FindBar.Overlay = FindHighlights;
        FindBar.Closed += FocusEditor;
    }
    public bool IsFindOpen => FindBar.IsOpen;
    public bool IsFindFocused => FindBar.IsKeyboardFocusWithin;
    public void OpenFind(bool replace) { if (_document is not null) FindBar.Open(replace); }
    public void CloseFind() => FindBar.Close();
    public void FindNext() => FindBar.Next();
    public void FindPrevious() => FindBar.Previous();
    public void ShowDocument(NoteDocument document)
    {
        _document = document;
        EditorHost.Content = document.Editor;
        LineGutter.Attach(document.Editor);
        MarkdownBlocks.Attach(document.Editor);
        CurrentLine.Attach(document.Editor);
        FindBar.Attach(document.Editor, () => document.SavedSelection);
        LineGutter.InvalidateMeasure();
        LineGutter.InvalidateVisual();
    }
    public void FocusEditor() { if (_document?.IsViewOnly == false) _document.Editor.Focus(); }
    public void Edit(string action)
    {
        if (_document is null || _document.IsViewOnly) return;
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
