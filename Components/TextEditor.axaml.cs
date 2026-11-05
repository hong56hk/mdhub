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
        SpellOverlay.CountChanged += count => SpellCountChanged?.Invoke(count);
    }
    // Item 74: spell checking of the shown Markdown editor.
    public SpellCheckOverlay Spelling => SpellOverlay;
    public event Action<int?>? SpellCountChanged;
    public bool NextMisspelling() => _document?.IsViewOnly == false && SpellOverlay.Next();
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
        SpellOverlay.Attach(document.Editor);
        FindBar.Attach(document.Editor, () => document.SavedSelection);
        LineGutter.InvalidateMeasure();
        LineGutter.InvalidateVisual();
    }
    // Lets another TextEditor host the document's editor control (a tab moved out of this split).
    public void ReleaseDocument() { EditorHost.Content = null; _document = null; }
    public void FocusEditor() { if (_document?.IsViewOnly == false) _document.Editor.Focus(); }
    public void Edit(string action)
    {
        if (_document is null || _document.IsViewOnly) return;
        var editor = _document.Editor;
        switch (action)
        {
            case "undo": _document.Undo(); break;
            case "redo": _document.Redo(); break;
            case "cut": editor.Cut(); break;
            case "copy": editor.Copy(); break;
            case "paste": editor.Paste(); break;
            case "select all": editor.SelectAll(); break;
        }
        editor.Focus();
    }
    public void Format(string action)
    {
        if (_document?.HasPreview != true || _document.Editor.IsReadOnly) return;
        var html = Mdhub.Services.EditorBehavior.Current.HtmlFormatting;
        if (!html && (_document.IsHtml || action.StartsWith("html-"))) return;
        var editor = _document.Editor;
        // Toolbar buttons and flyouts take focus, which collapses TextBox's live
        // selection. Format the range remembered while the editor had focus.
        if (!editor.IsFocused) _document.RestoreSelection();
        var edit = html ? HtmlFormatting.Apply(_document.Text, editor.SelectionStart, editor.SelectionEnd, action, _document.IsHtml)
            : MarkdownFormatting.Apply(_document.Text, editor.SelectionStart, editor.SelectionEnd, action);
        editor.SelectionStart = edit.Start;
        editor.SelectionEnd = edit.End;
        editor.SelectedText = edit.Replacement;
        editor.SelectionStart = edit.Start + edit.SelectionOffset;
        editor.SelectionEnd = editor.SelectionStart + edit.SelectionLength;
        editor.Focus();
    }
}
