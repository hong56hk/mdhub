using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Models;
using Mdhub.Views;
using Mdhub.Components;

if (args.Contains("--newfile-only")) { NewFileDialogTests.Run(); return; }
if (args.Contains("--reveal-only")) { RevealTests.Run(); return; }
if (args.Contains("--menu-only")) { MenuTests.Run(); return; }
if (args.Contains("--previewtoggle-only")) { PreviewToggleTests.Run(); return; }
if (args.Contains("--createtarget-only")) { CreateTargetTests.Run(); return; }
if (args.Contains("--searchindent-only")) { SearchIndentTests.Run(); return; }
if (args.Contains("--tabcycle-only")) { TabCycleTests.Run(); return; }
if (args.Contains("--formaticons-only")) { FormatIconTests.Run(); return; }
if (args.Contains("--previewsettings-only")) { PreviewSettingsTests.Run(); return; }
if (args.Contains("--export-only")) { ExportTests.Run(); return; }
if (args.Contains("--binary-only")) { BinaryFileTests.Run(); return; }
if (args.Contains("--strike-only")) { StrikeIconTests.Run(); return; }
if (args.Contains("--drag-only")) { ExplorerDragTests.Run(); return; }
if (args.Contains("--file-views")) { FileViewTests.Run(); return; }
if (args.Contains("--image-layout")) { ImageLayoutTests.Run(); return; }
if (args.Contains("--rename-only")) { RenameTests.Run(); return; }
if (args.Contains("--layout-only")) { ExplorerLayoutTests.Run(); return; }
if (args.Contains("--theme-only")) { ThemeTests.Run(); return; }
if (args.Contains("--recent-only")) { RecentTests.Run(); return; }
if (args.Contains("--l10n-only")) { LocalizationTests.Run(); return; }
if (args.Contains("--selection-only")) { SelectionTests.Run(); return; }
if (args.Contains("--quickopen-only")) { QuickOpenTests.Run(); return; }
if (args.Contains("--syntax-only")) { SyntaxTests.Run(); return; }
if (args.Contains("--explorerkeys-only")) { ExplorerShortcutTests.Run(); return; }
if (args.Contains("--explorerrefresh-only")) { ExplorerRefreshTests.Run(); return; }
if (args.Contains("--menupadding-only")) { MenuPaddingTests.Run(); return; }
if (args.Contains("--imagezoom-only")) { ImageZoomTests.Run(); return; }
if (args.Contains("--search-bench")) { SearchBench.Run(args); return; }
if (args.Contains("--search-only")) { SearchSpeedTests.Run(); WorkspaceSearchTests.Run(); SearchClickTests.Run(); return; }
if (args.Contains("--shortcuts-only")) { ShortcutTests.Run(); return; }
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var window = new MainWindow();
window.Show();
Dispatcher.UIThread.RunJobs();
var pickerCalls = 0;
string? pickerResult = null;
var pickerProperty = typeof(MainView).GetProperty("FolderPickerOverride", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
pickerProperty.SetValue(window.FindControl<MainView>("MainView"), new Func<Task<string?>>(() => { pickerCalls++; return Task.FromResult(pickerResult); }));
var folderNameButton = Find<Button>("FolderNameButton")!;
Require(Find<TextBlock>("FolderName")!.Text == "Open Folder" && folderNameButton.Cursor?.ToString() == new Cursor(StandardCursorType.Hand).ToString()
    && ToolTip.GetTip(folderNameButton) is string { Length: > 0 }, "Folder name row shows Open Folder with a hand cursor and tooltip");
folderNameButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Pump();
Require(pickerCalls == 1 && Find<TextBlock>("FolderName")!.Text == "Open Folder", "Clicking Open Folder with no folder open shows the folder picker; cancel changes nothing");
ClickMenu("Open _Folder…");
Pump();
Require(pickerCalls == 2, "File > Open Folder uses the same folder picker path");
var backgroundMenu = Find<TreeView>("FolderTree")!.ContextMenu!;
Find<FolderExplorer>("FolderExplorer")!.UpdateBackgroundMenu();
Require(string.Join("|", backgroundMenu.Items.Cast<Control>().Select(e => e is Separator ? "-" : ((MenuItem)e).Header)) == "New file|New folder|-|Paste|-|Reload"
    && backgroundMenu.Items.OfType<Separator>().All(s => s.Background is Avalonia.Media.ISolidColorBrush b && b.Color == Avalonia.Media.Colors.LightGray),
    "Empty-space menu lists New file, New folder, Paste, Reload with light gray separators");
Require(backgroundMenu.Items.OfType<MenuItem>().All(m => !m.IsEnabled), "Empty-space menu entries are disabled with no folder open");
Require(window.WindowDecorations == WindowDecorations.BorderOnly && window.ExtendClientAreaToDecorationsHint && window.CanResize,
    "Custom title bar removes native caption while retaining the resizable border");
Require(Avalonia.Controls.Chrome.WindowDecorationProperties.GetElementRole(Find<Border>("TitleBarDragRegion")!) == WindowDecorationsElementRole.TitleBar,
    "Empty menu-bar space is registered for native dragging and double-click maximize");
Find<Button>("MinimizeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Require(window.WindowState == WindowState.Minimized, "Custom minimize button minimizes the window");
window.WindowState = WindowState.Normal;
Find<Button>("MaximizeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Require(window.WindowState == WindowState.Maximized && Equals(ToolTip.GetTip(Find<Button>("MaximizeButton")!), "Restore"),
    "Custom maximize button maximizes and updates its restore label");
Find<Button>("MaximizeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Require(window.WindowState == WindowState.Normal, "Custom restore button returns to normal size");
Dispatcher.UIThread.RunJobs();
var tabs = Find<ListBox>("Tabs")!;
var first = (NoteDocument)tabs.SelectedItem!;
first.Editor.Text = "# Hello world\n\nA **bold** note.\n\n- First\n- Second\n\n| Name | Value |\n| --- | --- |\n| Test | OK |";
Dispatcher.UIThread.RunJobs();
Require(first.IsDirty && first.Title.StartsWith("●"), "Editing marks the document unsaved");

var newButton = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "+"));
newButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
Require(tabs.ItemCount == 2, "New opens a separate tab");
var second = (NoteDocument)tabs.SelectedItem!;
second.Editor.Text = "Second document";
tabs.SelectedItem = first;
Dispatcher.UIThread.RunJobs();
Require(first.Text.StartsWith("# Hello world") && second.Text == "Second document", "Switching tabs preserves independent unsaved buffers");
Require(ReferenceEquals(Find<ContentControl>("EditorHost")!.Content, first.Editor), "Selected tab displays its own editor");
first.Editor.SelectionStart = 2;
first.Editor.SelectionEnd = 7;
var boldButton = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "bold"));
boldButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
Require(first.Text.StartsWith("# **Hello** world"), "Toolbar formats the selected text");
first.Editor.Undo();
Require(first.Text.StartsWith("# Hello world"), "Formatting is undoable");
first.Editor.SelectionStart = 2;
first.Editor.SelectionEnd = 7;
tabs.SelectedItem = second;
Pump();
second.Editor.SelectionStart = 0;
second.Editor.SelectionEnd = 6;
tabs.SelectedItem = first;
Pump();
Require(first.Editor.SelectionStart == 2 && first.Editor.SelectionEnd == 7 && first.Editor.SelectedText == "Hello", "Selection survives switching away from a tab and back");
tabs.SelectedItem = second;
Pump();
Require(second.Editor.SelectedText == "Second", "Each tab restores its own selection");
tabs.SelectedItem = first;
Pump();
first.Editor.SelectionStart = first.Editor.SelectionEnd = 0;
var headerTexts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
Require(!headerTexts.Contains("EDITOR") && !headerTexts.Contains("PREVIEW") && !headerTexts.Contains("Live · Offline") && Find<TextBlock>("FileKind") is null,
    "Editor and preview pane header bars are removed");
Require(ReferenceEquals(Find<ContentControl>("EditorHost")!.Parent, Find<LineNumberGutter>("LineGutter")!.Parent)
    && Find<TextEditor>("TextEditor")!.GetVisualDescendants().OfType<Border>().All(b => b.Padding != new Thickness(16, 12)), "Editor fills its pane without a header");
Require(Find<TextBlock>("FileKindLabel")!.Text == "Markdown", "Status bar shows the file kind beside the encoding");

// Run a real event loop so dispatcher timers and async preview continuations execute.
using (var timer = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
    Dispatcher.UIThread.MainLoop(timer.Token);
var preview = Find<StackPanel>("PreviewContent")!;
Console.WriteLine("Preview status: " + Find<TextBlock>("Status")!.Text);
Console.WriteLine("Preview children: " + string.Join(", ", preview.Children.Select(c => c.GetType().Name)));
Require(preview.Children.OfType<TextBlock>().Any(t => t.FontSize == 28), "Markdown heading is rendered");
Require(preview.Children.OfType<Grid>().Any(), "Markdown table is rendered");
Require(Find<TextBlock>("Counts")!.Text?.Contains("words") == true, "Live file counts update");

var fixture = Path.GetFullPath("artifacts/ui-fixtures");
Directory.CreateDirectory(Path.Combine(fixture, "Projects"));
File.WriteAllText(Path.Combine(fixture, "Welcome.md"), first.Text);
File.WriteAllText(Path.Combine(fixture, "Ideas.txt"), "A plain-text note");
File.WriteAllBytes(Path.Combine(fixture, "attachment.bin"), [0, 1, 2]);
RunOperation("LoadRootAsync", fixture);
var tree = Find<TreeView>("FolderTree")!;
Require(tree.ItemCount == 4, "Folder tree lists subfolders and all file types");
tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(i => i.Header!.ToString()!.EndsWith("Welcome.md"));
Pump();
var opened = (NoteDocument)tabs.SelectedItem!;
Require(opened.Path == Path.Combine(fixture, "Welcome.md"), "Selecting a Markdown file opens it in a tab");
var count = tabs.ItemCount;
RunOperation("OpenPathAsync", opened.Path!);
Require(tabs.ItemCount == count, "Opening an existing file reuses its tab");
opened.Editor.Text += "\n\nSaved through the editor.";
RunOperation("SaveAsync", opened, false);
Require(File.ReadAllText(opened.Path!) == opened.Text && !opened.IsDirty, "Saving the active file persists edits and clears its dirty state");
Pump();

var explorer = Find<FolderExplorer>("FolderExplorer")!;
var creationFixture = Path.Combine(fixture, "Projects", "creation-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(creationFixture);
var seed = Path.Combine(creationFixture, "seed.md");
File.WriteAllText(seed, "Seed note");
tabs.SelectedItem = opened; Pump(); // The explorer row follows the active tab (item 29), so select the seed afterwards.
Await(explorer.RevealCreatedAsync(seed)); // Explorer selection must win over the active root-level file.
Pump();
Find<Button>("NewFileButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
EnterName("sibling.md");
var sibling = (NoteDocument)tabs.SelectedItem!;
Require(sibling.Path == Path.Combine(creationFixture, "sibling.md") && File.Exists(sibling.Path),
    "New file is created beside the selected file, ahead of the active-file fallback, and opens in a tab");
Find<Button>("NewFolderButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
EnterName("Sibling folder");
Require(Directory.Exists(Path.Combine(creationFixture, "Sibling folder")), "New folder is created beside the selected file");
Find<Button>("NewFolderButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
EnterName("Another folder");
// Item 42: the new folder is revealed and selected, so the next New folder goes inside it.
Require(Directory.Exists(Path.Combine(creationFixture, "Sibling folder", "Another folder")), "A selected folder creates the new item inside it");

// Item 42: with nothing selected the target is the open folder's root (checked below and in --createtarget-only),
// so select the seed file again to keep creating in this fixture.
Await(explorer.RevealCreatedAsync(seed)); Pump();
Find<Button>("NewFileButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
EnterName("fallback.txt");
Require(File.Exists(Path.Combine(creationFixture, "fallback.txt")), "A selected file creates the new file in its folder");
// The explorer reloads asynchronously after a create; wait for the reveal instead of relying on a fixed pump.
for (var wait = 0; wait < 40 && TreeNodes(tree.Items.OfType<TreeViewItem>()).Count(item => item.IsExpanded) < 2; wait++) Pump();
var allNodes = TreeNodes(tree.Items.OfType<TreeViewItem>()).ToArray();
Require(allNodes.Count(item => item.IsExpanded) >= 2, "Creating a nested item reveals its ancestor folders");
Find<Button>("CollapseAllButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Require(allNodes.All(item => !item.IsExpanded), "Collapse all also collapses nested expanded folders");

Find<Button>("NewFileButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
var duplicateDialog = window.OwnedWindows.Single();
duplicateDialog.GetVisualDescendants().OfType<TextBox>().Single().Text = "fallback.txt";
duplicateDialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Create")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Require(window.OwnedWindows.Count == 1 && File.ReadAllText(Path.Combine(creationFixture, "fallback.txt")) == "",
    "Duplicate names stay in the dialog without replacing the existing file");
duplicateDialog.GetVisualDescendants().OfType<TextBox>().Single().Text = "../escape.md";
duplicateDialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Create")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Require(window.OwnedWindows.Count == 1 && !File.Exists(Path.Combine(creationFixture, "..", "escape.md")), "Names cannot escape the target directory");
ClickDialog("Cancel");

tabs.SelectedItem = second; // Untitled document and no explorer selection means opened root.
tree.SelectedItem = null;
Require(explorer.GetCreationDirectory() == fixture, "An untitled active document falls back to the opened folder");
Find<Button>("NewFileButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
ClickDialog("Cancel");
Require(!File.Exists(Path.Combine(fixture, "Untitled.md")), "Cancelling creation does not create a file");

Find<Button>("ReloadFolderButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Pump();
Require(tree.ItemCount == 4 && tree.Items.OfType<TreeViewItem>().All(item => !item.IsExpanded), "Relocated Reload retains the existing refresh behavior");
tabs.SelectedItem = opened;
Pump();

var output = Path.GetFullPath("artifacts/layout-preview.png");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using (var frame = window.CaptureRenderedFrame()) frame?.Save(output, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
Console.WriteLine($"Screenshot: {output}");

ClickMenu("Toggle Folder Panel");
Require(!Find<FolderExplorer>("FolderExplorer")!.IsEffectivelyVisible, "View menu hides the folder component");
ClickMenu("Toggle Folder Panel");
ClickMenu("Toggle Preview");
Require(!Find<MdEditor>("MdEditor")!.IsVisible, "View menu hides the Markdown component");
ClickMenu("Toggle Preview");
Require(Find<FolderExplorer>("FolderExplorer")!.IsVisible && Find<MdEditor>("MdEditor")!.IsVisible, "View menu restores both panels");
ClickMenu("Toggle Word Wrap");
Require(opened.Editor.TextWrapping == Avalonia.Media.TextWrapping.Wrap, "Word wrap still applies to the active editor");
ClickMenu("Toggle Word Wrap");

var beforeShortcut = tabs.ItemCount;
opened.Editor.Focus();
opened.Editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.N, KeyModifiers = KeyModifiers.Control });
Require(tabs.ItemCount == beforeShortcut + 1, "Ctrl+N reaches MainView through the editor component");
ClickMenu("_Close Tab");
Require(tabs.ItemCount == beforeShortcut, "File menu closes a clean tab");

RunOperation("OpenPathAsync", Path.Combine(fixture, "Ideas.txt"));
Pump();
Require(!Find<StackPanel>("FormatBar")!.IsEnabled, "Plain text disables Markdown formatting");
Require(Find<TextBlock>("FileKindLabel")!.Text == "Plain text", "Status bar shows Plain text for .txt files");
// Plain text has no rendered preview (only Markdown does), so its text can never be reinterpreted as Markdown.
Require(!Find<MdEditor>("MdEditor")!.IsVisible && !preview.Children.Any(), "Plain text preview remains literal");
ClickMenu("_Close Tab");

tabs.SelectedItem = first;
ClickMenu("_Close Tab");
Require(window.OwnedWindows.Count == 1, "Closing an unsaved tab opens its confirmation dialog");
ClickDialog("Cancel");
Require(tabs.Items.Contains(first) && first.IsDirty, "Cancel preserves the unsaved tab");
var closeButton = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "×") && ReferenceEquals(b.DataContext, first));
closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
ClickDialog("Discard");
Require(!tabs.Items.Contains(first), "Tab close button routes Discard through the view");

tabs.SelectedItem = opened;
opened.Editor.Text += "\nSave on close";
ClickMenu("_Close Tab");
ClickDialog("Save");
Require(!tabs.Items.Contains(opened) && File.ReadAllText(opened.Path!) == opened.Text, "Save on close persists the selected tab before removing it");

// Line-number gutter
ClickMenu("_New");
var gutterTab = (NoteDocument)tabs.SelectedItem!;
var gutter = Find<LineNumberGutter>("LineGutter")!;
gutterTab.Editor.Text = "one\ntwo\r\nthree\nfour\nfive";
Pump();
Require(gutter is not null && gutter.IsVisible && ReferenceEquals(gutter.Editor, gutterTab.Editor) && gutter.Bounds.Width > 0, "Line-number gutter sits beside the active editor");
Require(gutter.LineCount == 5 && gutter.VisibleNumbers().Select(n => n.Number).SequenceEqual([1, 2, 3, 4, 5]), "Gutter numbers each line, including CRLF lines");
var smallWidth = gutter.Bounds.Width;
gutterTab.Editor.Text = string.Join("\n", Enumerable.Range(1, 1200).Select(i => $"line {i}"));
Pump();
Require(gutter.LineCount == 1200 && gutter.Bounds.Width > smallWidth, "Gutter line count follows typing and its width grows for larger numbers");
var tops = gutter.VisibleNumbers();
Require(tops.First().Number == 1 && tops.Zip(tops.Skip(1)).All(p => p.Second.Top > p.First.Top), "Gutter rows are ordered top to bottom");
var editorScroll = gutterTab.Editor.GetVisualDescendants().OfType<ScrollViewer>().First();
editorScroll.Offset = new Vector(0, 2000);
Pump();
var scrolled = gutter.VisibleNumbers();
var editorPresenter = gutterTab.Editor.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().First();
var expectedFirst = editorPresenter.TextLayout.TextLines.TakeWhile(_ => true).Select((l, i) => (l, i)).First(x => editorPresenter.TranslatePoint(new Point(0, editorPresenter.TextLayout.TextLines.Take(x.i).Sum(t => t.Height)), gutter)!.Value.Y + x.l.Height >= 0).i + 1;
Require(scrolled.Count > 0 && scrolled.First().Number > 1 && scrolled.First().Number == expectedFirst, "Gutter follows the editor scroll offset");
editorScroll.Offset = new Vector(0, 0);
Pump();
Require(gutter.VisibleNumbers().First().Number == 1, "Gutter returns to line 1 when scrolled back");
var longLine = string.Join(" ", Enumerable.Repeat("wrapping words", 60));
gutterTab.Editor.Text = $"short\n{longLine}\nafter";
ClickMenu("Toggle Word Wrap");
Pump();
var wrapped = gutter.VisibleNumbers();
Require(gutterTab.Editor.TextWrapping == Avalonia.Media.TextWrapping.Wrap && editorPresenter.TextLayout.TextLines.Count > 3 && wrapped.Select(n => n.Number).SequenceEqual([1, 2, 3])
    && wrapped[2].Top - wrapped[1].Top > (wrapped[1].Top - wrapped[0].Top) * 1.5 && editorScroll.Extent.Width <= editorScroll.Viewport.Width + 1, "With wrap on, the gutter numbers logical lines and leaves continuation rows blank");
ClickMenu("Toggle Word Wrap");
Pump();
Require(gutter.VisibleNumbers().Select(n => n.Number).SequenceEqual([1, 2, 3]) && wrapped[2].Top > gutter.VisibleNumbers()[2].Top, "Turning wrap off re-syncs the gutter");
gutterTab.Editor.Focus();
gutterTab.Editor.CaretIndex = 5;
gutterTab.Editor.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "!" });
Pump();
Require(gutterTab.Editor.Text!.StartsWith("short!\n") && gutterTab.Editor.CaretIndex == 6, "Typing and caret still work with the gutter");
gutterTab.Editor.Undo();
Pump();
Require(gutterTab.Editor.Text!.StartsWith("short\n"), "Undo still works with the gutter");
gutterTab.Editor.SelectionStart = 1;
gutterTab.Editor.SelectionEnd = 4;
Require(gutterTab.Editor.SelectedText == "hor", "Selection still works with the gutter");
gutterTab.Editor.CaretIndex = 3;
gutterTab.Editor.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "?" });
Pump();
tabs.SelectedItem = second;
Pump();
Require(ReferenceEquals(gutter.Editor, second.Editor), "Gutter follows the selected tab");
tabs.SelectedItem = gutterTab;
Pump();
Require(ReferenceEquals(gutter.Editor, gutterTab.Editor) && gutterTab.Editor.CaretIndex == 4 && gutterTab.Editor.CanUndo,
    "Each tab keeps its caret and undo history when switching tabs");
gutterTab.Editor.Undo();
Pump();
Require(gutterTab.Editor.Text!.StartsWith("short\n"), "Undo after switching tabs restores the tab's own text");
gutterTab.SavedText = gutterTab.Text;
RemoveTab(gutterTab);
tabs.SelectedItem = second;
Pump();

// Explorer context menu
var menuRoot = Path.Combine(Path.GetTempPath(), $"mdhub-menu-{Guid.NewGuid():N}");
Directory.CreateDirectory(Path.Combine(menuRoot, "Docs"));
File.WriteAllText(Path.Combine(menuRoot, "Docs", "inner.md"), "inner");
File.WriteAllText(Path.Combine(menuRoot, "note.md"), "note");
File.WriteAllText(Path.Combine(menuRoot, "gone.md"), "gone");
var mainView = window.FindControl<MainView>("MainView")!;
var revealed = new List<string>();
typeof(MainView).GetProperty("RevealOverride", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
    .SetValue(mainView, new Action<string>(revealed.Add));
var recycled = new List<string>();
typeof(MainView).GetProperty("RecycleOverride", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
    .SetValue(mainView, new Action<string>(path => { recycled.Add(path); if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path); }));
object? Clipboard() => typeof(MainView).GetProperty("ExplorerClipboard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(mainView);
RunOperation("LoadRootAsync", menuRoot);
Pump();
var noteItem = TreeItem(Path.Combine(menuRoot, "note.md"))!;
var menuEntries = noteItem.ContextMenu!.Items.Cast<Control>().ToArray();
Require(string.Join("|", menuEntries.Select(e => e is Separator ? "-" : ((MenuItem)e).Header)) == "Open with default app|-|Reveal in file explorer|-|Cut|Copy|Paste|Duplicate|-|Rename|Delete",
    "Explorer context menu lists Open with default app (item 52), Reveal, Cut, Copy, Duplicate, Rename, Delete in order with separators");
Require(menuEntries.OfType<Separator>().All(s => s.Background is Avalonia.Media.ISolidColorBrush b && b.Color == Avalonia.Media.Colors.LightGray),
    "Explorer context menu separators are light gray");
Require(TreeItem(Path.Combine(menuRoot, "Docs"))!.ContextMenu is not null, "Folders also have the explorer context menu");

ClickContext(Path.Combine(menuRoot, "note.md"), "RevealMenuItem");
WaitFor(() => revealed.Count == 1);
Require(revealed.Single() == Path.Combine(menuRoot, "note.md") && SelectedPath() == Path.Combine(menuRoot, "note.md"), "Reveal in file explorer reveals the item and selects it");
ClickContext(Path.Combine(menuRoot, "note.md"), "CutMenuItem");
WaitFor(() => Clipboard() is not null);
Require(Clipboard()!.ToString() == $"({Path.Combine(menuRoot, "note.md")}, True)", "Cut stores the path and cut state for paste");
ClickContext(Path.Combine(menuRoot, "Docs"), "CopyMenuItem");
WaitFor(() => Clipboard()!.ToString()!.EndsWith("False)"));
Require(Clipboard()!.ToString() == $"({Path.Combine(menuRoot, "Docs")}, False)" && File.Exists(Path.Combine(menuRoot, "note.md")), "Copy stores the path and copy state without changing files");

ClickContext(Path.Combine(menuRoot, "note.md"), "DuplicateMenuItem");
WaitFor(() => SelectedPath() == Path.Combine(menuRoot, "note-copy.md"));
Require(File.ReadAllText(Path.Combine(menuRoot, "note-copy.md")) == "note", "Duplicate creates '<name>-copy' beside the file and reveals it");
ClickContext(Path.Combine(menuRoot, "Docs"), "DuplicateMenuItem");
WaitFor(() => SelectedPath() == Path.Combine(menuRoot, "Docs-copy"));
Require(File.Exists(Path.Combine(menuRoot, "Docs-copy", "inner.md")), "Duplicate copies folders recursively");

RunOperation("OpenPathAsync", Path.Combine(menuRoot, "note.md"));
RunOperation("OpenPathAsync", Path.Combine(menuRoot, "Docs", "inner.md"));
var noteTab = tabs.Items.OfType<NoteDocument>().Single(d => d.Path == Path.Combine(menuRoot, "note.md"));
var innerTab = tabs.Items.OfType<NoteDocument>().Single(d => d.Path == Path.Combine(menuRoot, "Docs", "inner.md"));
ClickContext(Path.Combine(menuRoot, "note.md"), "RenameMenuItem");
WaitFor(() => explorer.IsRenaming);
Require(Find<TextBox>("ExplorerRenameBox")!.SelectedText == "note", "Inline rename selects the filename without extension");
void SubmitRename(string name)
{
    var box = Find<TextBox>("ExplorerRenameBox")!;
    box.Text = name;
    box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
    Pump();
}
SubmitRename("note-copy.md");
Require(explorer.IsRenaming && File.Exists(Path.Combine(menuRoot, "note.md")), "Inline rename rejects existing names");
SubmitRename("bad/name.md");
Require(explorer.IsRenaming, "Rename rejects names with paths");
SubmitRename("renamed.md");
WaitFor(() => SelectedPath() == Path.Combine(menuRoot, "renamed.md"));
Require(File.Exists(Path.Combine(menuRoot, "renamed.md")) && !File.Exists(Path.Combine(menuRoot, "note.md")) && noteTab.Path == Path.Combine(menuRoot, "renamed.md") && noteTab.Title == "renamed.md",
    "Rename renames the file, updates its open tab, and reveals it");
ClickContext(Path.Combine(menuRoot, "Docs"), "RenameMenuItem");
WaitFor(() => explorer.IsRenaming);
SubmitRename("Folder");
WaitFor(() => SelectedPath() == Path.Combine(menuRoot, "Folder"));
Require(innerTab.Path == Path.Combine(menuRoot, "Folder", "inner.md"), "Renaming a folder updates tabs for files inside it");

ClickContext(Path.Combine(menuRoot, "gone.md"), "DeleteMenuItem");
WaitFor(() => window.OwnedWindows.Count == 1);
ClickDialog("Cancel");
Require(File.Exists(Path.Combine(menuRoot, "gone.md")), "Cancelling delete keeps the item");
ClickContext(Path.Combine(menuRoot, "gone.md"), "DeleteMenuItem");
WaitFor(() => window.OwnedWindows.Count == 1);
ClickDialog("Move the file to Recycle Bin"); // item 53: the button names the action
WaitFor(() => !File.Exists(Path.Combine(menuRoot, "gone.md")) && TreeItem(Path.Combine(menuRoot, "gone.md")) is null);
Require(recycled.SequenceEqual([Path.Combine(menuRoot, "gone.md")]) && !Directory.Exists(Path.Combine(menuRoot, ".md-trash")),
    "Delete confirms, sends the item to the Recycle Bin (not .md-trash), and refreshes the tree");

noteTab.Editor.Text += " edited";
Pump();
ClickContext(Path.Combine(menuRoot, "renamed.md"), "DeleteMenuItem");
WaitFor(() => window.OwnedWindows.Count == 1);
ClickDialog("Move the file to Recycle Bin"); // item 53: the button names the action
WaitFor(() => window.OwnedWindows.Count == 1);
ClickDialog("Cancel");
Require(File.Exists(Path.Combine(menuRoot, "renamed.md")) && tabs.Items.Contains(noteTab), "Cancelling the unsaved-changes prompt cancels the delete");
ClickContext(Path.Combine(menuRoot, "renamed.md"), "DeleteMenuItem");
WaitFor(() => window.OwnedWindows.Count == 1);
ClickDialog("Move the file to Recycle Bin"); // item 53: the button names the action
WaitFor(() => window.OwnedWindows.Count == 1);
ClickDialog("Discard");
WaitFor(() => !tabs.Items.Contains(noteTab));
Require(!File.Exists(Path.Combine(menuRoot, "renamed.md")), "Deleting an edited open file prompts, then closes its tab");
ClickContext(Path.Combine(menuRoot, "Folder"), "DeleteMenuItem");
WaitFor(() => window.OwnedWindows.Count == 1);
ClickDialog("Move the file to Recycle Bin"); // item 53: the button names the action
WaitFor(() => !tabs.Items.Contains(innerTab));
Require(!Directory.Exists(Path.Combine(menuRoot, "Folder")) && recycled.Last() == Path.Combine(menuRoot, "Folder"), "Deleting a folder closes clean tabs for files inside it");
// Empty-space menu
MenuItem Bg(string name) { explorer.UpdateBackgroundMenu(); return Find<TreeView>("FolderTree")!.ContextMenu!.Items.OfType<MenuItem>().Single(m => m.Name == name); }
MenuItem RowPaste(string path) { explorer.UpdateBackgroundMenu(); return TreeItem(path)!.ContextMenu!.Items.OfType<MenuItem>().Single(m => m.Name == "PasteMenuItem"); }
void ClickBg(string name) { Bg(name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(); }
Require(Bg("BackgroundNewFileMenuItem").IsEnabled && Bg("BackgroundReloadMenuItem").IsEnabled, "Empty-space menu entries are enabled with a folder open");
Directory.CreateDirectory(Path.Combine(menuRoot, "Sub"));
File.WriteAllText(Path.Combine(menuRoot, "Sub", "moved.md"), "moved");
File.WriteAllText(Path.Combine(menuRoot, "paste.md"), "paste");
ClickBg("BackgroundReloadMenuItem");
WaitFor(() => TreeItem(Path.Combine(menuRoot, "paste.md")) is not null);
Require(TreeItem(Path.Combine(menuRoot, "Sub")) is not null, "Empty-space Reload refreshes the tree");
RunOperation("OpenPathAsync", Path.Combine(menuRoot, "Sub", "moved.md"));
var movedTab = tabs.Items.OfType<NoteDocument>().Single(d => d.Path == Path.Combine(menuRoot, "Sub", "moved.md"));
TreeItem(Path.Combine(menuRoot, "Sub"))!.IsSelected = true;
ClickBg("BackgroundNewFileMenuItem");
WaitFor(() => window.OwnedWindows.Count == 1);
EnterName("root-new.md");
WaitFor(() => File.Exists(Path.Combine(menuRoot, "root-new.md")));
Require(!File.Exists(Path.Combine(menuRoot, "Sub", "root-new.md")), "Empty-space New file creates in the opened root folder");
ClickBg("BackgroundNewFolderMenuItem");
WaitFor(() => window.OwnedWindows.Count == 1);
EnterName("Root folder");
Require(Directory.Exists(Path.Combine(menuRoot, "Root folder")), "Empty-space New folder creates in the opened root folder");
var clipProperty = typeof(MainView).GetProperty("ExplorerClipboard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
clipProperty.GetSetMethod(true)!.Invoke(mainView, [null]);
Require(!Bg("BackgroundPasteMenuItem").IsEnabled, "Paste is disabled when the clipboard is empty");
Require(!RowPaste(Path.Combine(menuRoot, "paste.md")).IsEnabled && !RowPaste(Path.Combine(menuRoot, "Sub")).IsEnabled, "Row Paste (file and folder menus) is disabled when the clipboard is empty");
ClickContext(Path.Combine(menuRoot, "paste.md"), "CopyMenuItem");
WaitFor(() => Clipboard() is not null);
Require(Bg("BackgroundPasteMenuItem").IsEnabled, "Paste is enabled after Copy");
ClickBg("BackgroundPasteMenuItem");
WaitFor(() => SelectedPath() == Path.Combine(menuRoot, "paste-copy.md"));
Require(File.ReadAllText(Path.Combine(menuRoot, "paste-copy.md")) == "paste" && File.Exists(Path.Combine(menuRoot, "paste.md")) && Clipboard() is not null,
    "Paste after Copy copies into the root with a unique name, reveals it, and keeps the clipboard");
Require(RowPaste(Path.Combine(menuRoot, "Sub")).IsEnabled && Mdhub.Components.ShortcutKeys.MenuText(RowPaste(Path.Combine(menuRoot, "Sub"))) == "Ctrl+V", "Row Paste is enabled after Copy and shows Ctrl+V");
ClickContext(Path.Combine(menuRoot, "Sub"), "PasteMenuItem");
WaitFor(() => File.Exists(Path.Combine(menuRoot, "Sub", "paste.md")));
Require(File.ReadAllText(Path.Combine(menuRoot, "Sub", "paste.md")) == "paste", "Paste on a folder pastes inside it");
TreeItem(Path.Combine(menuRoot, "Sub"))!.IsExpanded = true;
WaitFor(() => TreeItem(Path.Combine(menuRoot, "Sub", "moved.md")) is not null);
ClickContext(Path.Combine(menuRoot, "Sub", "moved.md"), "PasteMenuItem");
WaitFor(() => File.Exists(Path.Combine(menuRoot, "Sub", "paste-copy.md")));
Require(File.ReadAllText(Path.Combine(menuRoot, "Sub", "paste-copy.md")) == "paste", "Paste on a file pastes beside it");
Await(explorer.RevealCreatedAsync(Path.Combine(menuRoot, "Sub", "moved.md")));
ClickContext(Path.Combine(menuRoot, "Sub", "moved.md"), "CutMenuItem");
WaitFor(() => Clipboard()!.ToString()!.EndsWith("True)"));
ClickBg("BackgroundPasteMenuItem");
WaitFor(() => SelectedPath() == Path.Combine(menuRoot, "moved.md"));
Require(!File.Exists(Path.Combine(menuRoot, "Sub", "moved.md")) && movedTab.Path == Path.Combine(menuRoot, "moved.md") && Clipboard() is null && !Bg("BackgroundPasteMenuItem").IsEnabled,
    "Paste after Cut moves into the root, updates the open tab, and clears the clipboard");
RemoveTab(movedTab);

// Auto-reload: Save As into the folder and external changes, keeping expansion
File.WriteAllText(Path.Combine(menuRoot, "Root folder", "kept.md"), "kept");
ClickBg("BackgroundReloadMenuItem");
WaitFor(() => TreeItem(Path.Combine(menuRoot, "Root folder")) is not null);
Await(explorer.RevealCreatedAsync(Path.Combine(menuRoot, "Root folder", "kept.md")));
Require(TreeItem(Path.Combine(menuRoot, "Root folder"))!.IsExpanded, "Revealing a nested file expands its folder");
ClickBg("BackgroundReloadMenuItem");
WaitFor(() => TreeItem(Path.Combine(menuRoot, "Root folder", "kept.md")) is not null);
Require(TreeItem(Path.Combine(menuRoot, "Root folder"))!.IsExpanded, "Reload keeps expanded folders expanded");
var savePickerProperty = typeof(MainView).GetProperty("SaveFilePickerOverride", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
var saveTarget = Path.Combine(menuRoot, "Sub", "saved-as.md");
savePickerProperty.SetValue(mainView, new Func<NoteDocument, Task<string?>>(_ => Task.FromResult<string?>(saveTarget)));
ClickMenu("_New");
var saveTab = (NoteDocument)tabs.SelectedItem!;
saveTab.Editor.Text = "saved via save as";
ClickMenu("Save _As…");
WaitFor(() => SelectedPath() == saveTarget);
Require(File.Exists(saveTarget) && saveTab.Path == saveTarget && TreeItem(Path.Combine(menuRoot, "Sub"))!.IsExpanded && TreeItem(Path.Combine(menuRoot, "Root folder"))!.IsExpanded,
    "Save As to a new path in the folder refreshes the tree, reveals the file, and keeps expanded folders");
savePickerProperty.SetValue(mainView, null);
RemoveTab(saveTab);
File.WriteAllText(Path.Combine(menuRoot, "Root folder", "external.md"), "from another program");
WaitFor(() => TreeItem(Path.Combine(menuRoot, "Root folder", "external.md")) is not null);
Require(TreeItem(Path.Combine(menuRoot, "Root folder"))!.IsExpanded && TreeItem(Path.Combine(menuRoot, "Sub"))!.IsExpanded,
    "File watcher refreshes the tree for external changes and keeps expanded folders");
Directory.CreateDirectory(Path.Combine(menuRoot, ".git"));
File.WriteAllText(Path.Combine(menuRoot, ".git", "HEAD"), "ref");
Pump(); Pump();
Require(TreeItem(Path.Combine(menuRoot, ".git", "HEAD")) is null, "File watcher ignores .git changes inside collapsed folders");
tabs.SelectedItem = second;
Pump();
Directory.Delete(menuRoot, recursive: true);

Find<Button>("CloseWindowButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Require(window.IsVisible && window.OwnedWindows.Count == 1, "Custom close button preserves the unsaved-change confirmation");
ClickDialog("Cancel");
Require(window.IsVisible && second.IsDirty, "Cancel keeps the application and unsaved text open");
second.SavedText = second.Text;
window.Close();
Require(!window.IsVisible, "Clean window closes normally");
var sortRoot = Path.Combine(Path.GetTempPath(), $"mdhub-sort-{Guid.NewGuid():N}");
try
{
    foreach (var d in new[] { "beta", "Alpha", "gamma" }) Directory.CreateDirectory(Path.Combine(sortRoot, d));
    foreach (var f in new[] { "b.md", "A.md", "c.txt" }) File.WriteAllText(Path.Combine(sortRoot, f), "");
    foreach (var f in new[] { "zeta.md", "Beta.md", "alpha.md" }) File.WriteAllText(Path.Combine(sortRoot, "beta", f), "");
    Directory.CreateDirectory(Path.Combine(sortRoot, "beta", "Zsub"));
    Directory.CreateDirectory(Path.Combine(sortRoot, "beta", "asub"));
    var top = Mdhub.Services.FolderService.ReadAsync(sortRoot).GetAwaiter().GetResult().Select(e => Path.GetFileName(e.Path)).ToArray();
    Require(top.SequenceEqual(new[] { "Alpha", "beta", "gamma", "A.md", "b.md", "c.txt" }), "Explorer lists folders first, then files, each A-Z case-insensitive");
    var nested = Mdhub.Services.FolderService.ReadAsync(Path.Combine(sortRoot, "beta")).GetAwaiter().GetResult().Select(e => Path.GetFileName(e.Path)).ToArray();
    Require(nested.SequenceEqual(new[] { "asub", "Zsub", "alpha.md", "Beta.md", "zeta.md" }), "Explorer sort order applies at nested levels");
}
finally { Directory.Delete(sortRoot, recursive: true); }
var replaceRoot = Path.Combine(Path.GetTempPath(), $"mdhub-open-{Guid.NewGuid():N}");
var createdWindows = new List<MainWindow>();
var factoryProperty = typeof(MainView).GetProperty("WindowFactory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
factoryProperty.SetValue(null, new Func<MainWindow>(() => { var created = new MainWindow(); createdWindows.Add(created); return created; }));
try
{
    var firstFolder = Directory.CreateDirectory(Path.Combine(replaceRoot, "First")).FullName;
    var secondFolder = Directory.CreateDirectory(Path.Combine(replaceRoot, "Second")).FullName;
    File.WriteAllText(Path.Combine(secondFolder, "only.md"), "");
    var w1 = new MainWindow();
    w1.Show();
    Pump();
    var view1 = w1.FindControl<MainView>("MainView")!;
    string? w1Pick = null;
    pickerProperty.SetValue(view1, new Func<Task<string?>>(() => Task.FromResult(w1Pick)));
    T In<T>(Window w, string name) where T : Control => w.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    w1Pick = firstFolder;
    In<Button>(w1, "FolderNameButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump();
    Require(In<TextBlock>(w1, "FolderName").Text == "First" && createdWindows.Count == 0, "With no folder open, Open Folder loads in the current window");
    w1Pick = secondFolder;
    In<Button>(w1, "FolderNameButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    var waitUntil = DateTime.UtcNow.AddSeconds(10);
    while ((createdWindows.Count == 0 || createdWindows[0].GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "FolderName")?.Text != "Second") && DateTime.UtcNow < waitUntil) Pump();
    Require(createdWindows.Count == 1 && createdWindows[0].IsVisible && In<TextBlock>(createdWindows[0], "FolderName").Text == "Second"
        && In<TreeView>(createdWindows[0], "FolderTree").Items.OfType<TreeViewItem>().Single().Header!.ToString()!.EndsWith("only.md"),
        "With a folder open, Open Folder opens the picked folder in a new window");
    Require(In<TextBlock>(w1, "FolderName").Text == "First", "The current window keeps its folder when another opens in a new window");
    var w2 = createdWindows[0];
    w1Pick = firstFolder.ToUpperInvariant() + Path.DirectorySeparatorChar;
    var activationProperty = typeof(MainView).GetProperty("ActivationCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    var activationsBefore = (int)activationProperty.GetValue(view1)!;
    ClickMenuIn(w1, "Open _Folder…");
    Pump();
    Require(createdWindows.Count == 1 && (int)activationProperty.GetValue(view1)! == activationsBefore + 1 && In<TextBlock>(w1, "FolderName").Text == "First",
        "Opening the same folder (normalized, case-insensitive) focuses the current window instead of opening another");
    var w2Tabs = In<ListBox>(w2, "Tabs");
    var w2Doc = (NoteDocument)w2Tabs.SelectedItem!;
    w2Doc.Editor.Text = "unsaved in second window";
    Pump();
    var w1Tabs = In<ListBox>(w1, "Tabs");
    Require(!w1Tabs.Items.OfType<NoteDocument>().Any(d => d.Text == "unsaved in second window"), "Each window has its own tabs");
    foreach (var d in w1Tabs.Items.OfType<NoteDocument>()) d.SavedText = d.Text;
    In<Button>(w1, "CloseWindowButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump();
    Require(!w1.IsVisible && w2.IsVisible && w2Doc.IsDirty && In<TextBlock>(w2, "FolderName").Text == "Second", "Closing one window leaves the other open and unchanged");
    In<Button>(w2, "CloseWindowButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump();
    Require(w2.IsVisible && w2.OwnedWindows.Count == 1, "Each window keeps its own unsaved-close prompt");
    w2.OwnedWindows.Single().GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Discard")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump();
    Require(!w2.IsVisible, "Discarding closes the second window");
    Require(Mdhub.App.ShutdownModeSetting == ShutdownMode.OnLastWindowClose, "App exits when the last window closes (ShutdownMode.OnLastWindowClose)");
}
finally { factoryProperty.SetValue(null, null); Directory.Delete(replaceRoot, recursive: true); }
var imageRoot = Path.Combine(Path.GetTempPath(), $"mdhub-image-{Guid.NewGuid():N}");
try
{
    Directory.CreateDirectory(imageRoot);
    var notePath = Path.Combine(imageRoot, "note.md");
    File.WriteAllText(notePath, "ab");
    var w3 = new MainWindow();
    w3.Show();
    Pump();
    var view3 = w3.FindControl<MainView>("MainView")!;
    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    void Run3(string method, params object[] args) { var task = (Task)typeof(MainView).GetMethod(method, flags)!.Invoke(view3, args)!; while (!task.IsCompleted) Pump(); task.GetAwaiter().GetResult(); }
    var clipboardImage = typeof(MainView).Assembly.GetType("Mdhub.Views.ClipboardImage")!;
    var overrideProperty = typeof(MainView).GetProperty("ClipboardImageOverride", flags)!;
    void Clip(string? file, byte[]? png) => overrideProperty.SetValue(view3, MakeReader(file is null && png is null ? null : Activator.CreateInstance(clipboardImage, file, png)));
    Delegate MakeReader(object? value)
    {
        var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(clipboardImage);
        var task = fromResult.Invoke(null, [value]);
        var funcType = typeof(Func<>).MakeGenericType(typeof(Task<>).MakeGenericType(clipboardImage));
        return System.Linq.Expressions.Expression.Lambda(funcType, System.Linq.Expressions.Expression.Constant(task)).Compile();
    }
    var stamp = new DateTime(2026, 10, 29, 13, 5, 9);
    typeof(MainView).GetProperty("PasteClock", flags)!.SetValue(view3, new Func<DateTime>(() => stamp));
    var w3Tabs = w3.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "Tabs");
    var w3Preview = w3.GetVisualDescendants().OfType<StackPanel>().Single(c => c.Name == "PreviewContent");
    void CtrlV(NoteDocument d)
    {
        d.Editor.Focus();
        d.Editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.V, KeyModifiers = KeyModifiers.Control });
        Pump();
    }
    Run3("OpenPathAsync", notePath);
    var note = (NoteDocument)w3Tabs.SelectedItem!;
    var assets = Path.Combine(imageRoot, ".assets");
    var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    Clip(null, png);
    note.Editor.SelectionStart = note.Editor.SelectionEnd = 1;
    CtrlV(note);
    var bitmapName = "note-20261029130509.png";
    Require(Directory.Exists(assets) && File.ReadAllBytes(Path.Combine(assets, bitmapName)).SequenceEqual(png), "Pasting bitmap data creates .assets and saves <note>-<timestamp>.png");
    Require(note.Editor.Text == $"a![note-20261029130509](.assets/{bitmapName})b", "Bitmap paste inserts a relative image link at the caret");
    note.Editor.Undo();
    Pump();
    Require(note.Editor.Text == "ab", "Image link insertion is undoable");

    var photo = Path.Combine(Path.GetTempPath(), $"photo-{Guid.NewGuid():N}.JPG");
    File.WriteAllBytes(photo, png);
    Clip(photo, null);
    note.Editor.SelectionStart = note.Editor.SelectionEnd = 2;
    CtrlV(note);
    Require(File.Exists(Path.Combine(assets, "note-20261029130509.JPG")) && note.Editor.Text == "ab![note-20261029130509](.assets/note-20261029130509.JPG)",
        "Pasting a copied image file keeps its original extension");
    File.Delete(photo);

    Clip(null, png);
    note.Editor.Text = "";
    CtrlV(note);
    Require(File.Exists(Path.Combine(assets, "note-20261029130509-1.png")) && note.Editor.Text == "![note-20261029130509-1](.assets/note-20261029130509-1.png)",
        "Name collisions in .assets add -1");

    Clip(null, null);
    overrideProperty.SetValue(view3, null);
    Await(Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(TopLevel.GetTopLevel(view3)!.Clipboard!, "plain text"));
    note.Editor.Text = "";
    var assetCount = Directory.GetFiles(assets).Length;
    CtrlV(note);
    WaitFor(() => note.Editor.Text == "plain text");
    Require(note.Editor.Text == "plain text" && Directory.GetFiles(assets).Length == assetCount, "Plain-text paste is unchanged and saves no asset");

    var savePicker = typeof(MainView).GetProperty("SaveFilePickerOverride", flags)!;
    string? pick = null;
    savePicker.SetValue(view3, new Func<NoteDocument, Task<string?>>(_ => Task.FromResult(pick)));
    note.Editor.Focus();
    note.Editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.N, KeyModifiers = KeyModifiers.Control });
    Pump();
    var untitled = (NoteDocument)w3Tabs.SelectedItem!;
    Require(untitled.Path is null, "New untitled document for image paste");
    Clip(null, png);
    assetCount = Directory.GetFiles(assets).Length;
    CtrlV(untitled);
    Require(untitled.Path is null && untitled.Editor.Text is null or "" && Directory.GetFiles(assets).Length == assetCount,
        "Cancelling the save for an untitled document cancels the image paste");
    pick = Path.Combine(imageRoot, "fresh.md");
    CtrlV(untitled);
    WaitFor(() => untitled.Path == pick);
    Require(File.Exists(Path.Combine(assets, "fresh-20261029130509.png")) && untitled.Editor.Text == "![fresh-20261029130509](.assets/fresh-20261029130509.png)",
        "Untitled document is saved first, then the image is pasted next to it");
    savePicker.SetValue(view3, null);
    overrideProperty.SetValue(view3, null);

    var wide = Path.Combine(assets, "wide image.png");
    using (var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(3000, 40), new Vector(96, 96))) bitmap.Save(wide);
    var absolute = Path.Combine(imageRoot, "abs.png");
    File.Copy(wide, absolute);
    w3Tabs.SelectedItem = note;
    Pump();
    note.Editor.Text = $"![wide](.assets/wide%20image.png)\n\n![abs]({absolute.Replace('\\', '/')})\n\n![missing](.assets/nope.png)\n\n![remote](https://example.com/x.png)";
    List<Image> Images() => w3Preview.GetVisualDescendants().OfType<Image>().ToList();
    WaitFor(() => Images().Count == 2);
    Pump();
    var images = Images();
    Require(images.Any(i => Equals(i.Tag, wide)) && images.Any(i => string.Equals(i.Tag as string, absolute, StringComparison.OrdinalIgnoreCase)),
        "Preview renders local images relative to the note and by absolute path");
    var wideImage = images.Single(i => Equals(i.Tag, wide));
    Require(wideImage.Bounds.Width > 0 && wideImage.Bounds.Width <= w3Preview.Bounds.Width + 0.5 && wideImage.Bounds.Width < 3000, "Wide local image is scaled down to fit the preview width");
    string Texts() => string.Join("\n", w3Preview.GetVisualDescendants().OfType<TextBlock>().Select(InlineString));
    Require(Texts().Contains("[Image: missing]"), "Missing local image keeps the label fallback");
    Require(Texts().Contains("[Image: remote]"), "Remote image URLs stay labels (no network)");
    foreach (var d in w3Tabs.Items.OfType<NoteDocument>()) d.SavedText = d.Text;
    w3.Close();
}
finally { Directory.Delete(imageRoot, recursive: true); }
{
    var w4 = new MainWindow { Width = 1200, Height = 700 };
    w4.Show();
    Pump();
    T F<T>(string name) where T : Control => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(w4).OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? w4.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    void KeyOn(InputElement target, Key key, KeyModifiers modifiers = KeyModifiers.None) { target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers }); Pump(); }
    var tabs4 = F<ListBox>("Tabs");
    var doc = (NoteDocument)tabs4.SelectedItem!;
    var ed = doc.Editor;
    ed.Text = "alpha beta alpha\ngamma alpha beta\nalpha";
    Pump();
    var bar = F<FindBar>("FindBar");
    var findInput = F<TextBox>("FindInput");
    var findCount = F<TextBlock>("FindCount");
    var overlay = F<FindHighlightOverlay>("FindHighlights");
    (int, int) Cur() => bar.CurrentIndex >= 0 ? (bar.Matches[bar.CurrentIndex].Start, bar.Matches[bar.CurrentIndex].End) : (-1, -1);
    ed.Focus();
    ed.SelectionStart = 6; ed.SelectionEnd = 10;
    KeyOn(ed, Key.F, KeyModifiers.Control);
    Require(bar.IsVisible && !F<StackPanel>("ReplaceRow").IsVisible && bar.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right && bar.VerticalAlignment == Avalonia.Layout.VerticalAlignment.Top,
        "Ctrl+F opens the find bar at the top right without the replace row");
    Require(findInput.Text == "beta" && findInput.IsFocused && findInput.SelectionStart == 0 && findInput.SelectionEnd == 4, "Selected text seeds the find field, which is focused with its text selected");
    Require(findCount.Text == "1 of 2" && Cur() == (6, 10), "Count shows the current match of all matches");
    findInput.Text = "alpha";
    Pump();
    Require(findCount.Text == "2 of 4" && Cur() == (11, 16), "Typing searches from the caret and selects the next match");
    KeyOn(findInput, Key.Enter);
    Require(findCount.Text == "3 of 4" && Cur().Item1 == 23, "Enter goes to the next match");
    KeyOn(findInput, Key.F3);
    Require(findCount.Text == "4 of 4" && Cur().Item1 == 34, "F3 goes to the next match");
    KeyOn(findInput, Key.F3);
    Require(findCount.Text == "1 of 4" && Cur().Item1 == 0, "Next wraps to the first match");
    KeyOn(findInput, Key.F3, KeyModifiers.Shift);
    Require(findCount.Text == "4 of 4", "Shift+F3 goes to the previous match (wrapping)");
    KeyOn(findInput, Key.Enter, KeyModifiers.Shift);
    Require(findCount.Text == "3 of 4" && Cur() == (23, 28), "Shift+Enter goes to the previous match");
    F<Button>("FindNextButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    F<Button>("FindPreviousButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    Require(findCount.Text == "3 of 4", "Arrow buttons navigate");
    var rects = overlay.Rectangles().ToList();
    Require(overlay.MatchCount == 4 && rects.Count == 4 && rects.Count(r => r.IsCurrent) == 1 && rects.All(r => r.Rect.Width > 0), "All matches are highlighted, the current one distinctly");
    ed.Text += " alpha";
    Pump();
    Require(findCount.Text.EndsWith("of 5"), "Count updates live when the document changes");
    F<Avalonia.Controls.Primitives.ToggleButton>("MatchCaseToggle").IsChecked = true;
    findInput.Text = "Alpha";
    Pump();
    Require(findCount.Text == "No results" && overlay.MatchCount == 0, "Match case toggle restricts matches");
    F<Avalonia.Controls.Primitives.ToggleButton>("MatchCaseToggle").IsChecked = false;
    Pump();
    Require(findCount.Text.EndsWith("of 5"), "Turning match case off finds all again");
    ed.Text = "cat concat cat";
    findInput.Text = "cat";
    F<Avalonia.Controls.Primitives.ToggleButton>("WholeWordToggle").IsChecked = true;
    Pump();
    Require(findCount.Text.EndsWith("of 2"), "Whole word toggle skips matches inside words");
    F<Avalonia.Controls.Primitives.ToggleButton>("WholeWordToggle").IsChecked = false;
    ed.Text = "alpha beta alpha\ngamma alpha beta\nalpha";
    F<Avalonia.Controls.Primitives.ToggleButton>("RegexToggle").IsChecked = true;
    findInput.Text = "(al";
    Pump();
    Require(findInput.Classes.Contains("invalid") && findCount.Text == "No results" && overlay.MatchCount == 0 && bar.IsVisible, "Invalid regex shows a red border and no results without crashing");
    findInput.Text = "al.ha|gam+a";
    Pump();
    Require(!findInput.Classes.Contains("invalid") && findCount.Text.EndsWith("of 5"), "Valid regex clears the error and matches");
    F<Avalonia.Controls.Primitives.ToggleButton>("RegexToggle").IsChecked = false;
    findInput.Text = "alpha";
    Pump();
    ed.SelectionStart = 0; ed.SelectionEnd = 17;
    F<Avalonia.Controls.Primitives.ToggleButton>("FindInSelectionToggle").IsChecked = true;
    Pump();
    Require(findCount.Text == "1 of 2" && bar.SearchRange == (0, 17), "Find in selection searches only the selection that existed when toggled on");
    KeyOn(findInput, Key.Enter); KeyOn(findInput, Key.Enter);
    Require(findCount.Text == "1 of 2" && bar.SearchRange == (0, 17), "Navigating keeps the original selection range");
    F<Avalonia.Controls.Primitives.ToggleButton>("FindInSelectionToggle").IsChecked = false;
    Pump();
    Require(findCount.Text.EndsWith("of 4") && bar.SearchRange is null, "Turning find in selection off searches the whole document");

    ed.Focus();
    KeyOn(ed, Key.Escape);
    Require(!bar.IsVisible && ed.IsFocused && overlay.MatchCount == 0, "Esc in the editor closes the bar and clears highlights");
    ed.Text = "cat Cat cat";
    ed.SelectionStart = ed.SelectionEnd = 0;
    KeyOn(ed, Key.H, KeyModifiers.Control);
    Require(bar.IsVisible && F<StackPanel>("ReplaceRow").IsVisible && findInput.IsFocused, "Ctrl+H opens the bar with the replace row expanded");
    findInput.Text = "cat";
    F<TextBox>("ReplaceInput").Text = "dog";
    F<Avalonia.Controls.Primitives.ToggleButton>("PreserveCaseToggle").IsChecked = true;
    Pump();
    Require(findCount.Text == "1 of 3", "Replace starts at the first match");
    F<Button>("ReplaceButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    Require(ed.Text == "dog Cat cat" && findCount.Text == "1 of 2" && Cur() == (4, 7), "Replace replaces the current match and moves to the next");
    F<Button>("ReplaceButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    Require(ed.Text == "dog Dog cat", "Preserve case keeps the matched capitalization");
    ed.Undo(); Pump();
    Require(ed.Text == "dog Cat cat", "Each Replace is one undo step");
    Require(findCount.Text.EndsWith("of 2"), "Count updates after undo");
    F<TextBox>("ReplaceInput").Text = "bird";
    F<Button>("ReplaceAllButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    Require(ed.Text == "dog Bird bird" && findCount.Text == "No results", "Replace all replaces every match");
    ed.Undo(); Pump();
    Require(ed.Text == "dog Cat cat", "Replace all is one undo step");
    F<Avalonia.Controls.Primitives.ToggleButton>("PreserveCaseToggle").IsChecked = false;
    F<Avalonia.Controls.Primitives.ToggleButton>("RegexToggle").IsChecked = true;
    findInput.Text = @"(\w+) (\w+)";
    F<TextBox>("ReplaceInput").Text = "$2-$1";
    F<Button>("ReplaceAllButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    Require(ed.Text == "Cat-dog cat", "Regex replace substitutes groups");
    ed.Undo(); Pump();
    F<Avalonia.Controls.Primitives.ToggleButton>("RegexToggle").IsChecked = false;
    ed.Text = "alpha beta alpha\ngamma alpha beta\nalpha";
    findInput.Text = "alpha";
    F<Avalonia.Controls.Primitives.ToggleButton>("MatchCaseToggle").IsChecked = true;
    Pump();
    try
    {
        Directory.CreateDirectory("artifacts");
        var editorView = w4.GetVisualDescendants().OfType<TextEditor>().First();
        var size = new PixelSize((int)editorView.Bounds.Width, 140);
        using var shot = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
        shot.Render(editorView);
        shot.Save(Path.GetFullPath("artifacts/find-bar.png"));
        Console.WriteLine("Saved " + Path.GetFullPath("artifacts/find-bar.png"));
    }
    catch (Exception ex) { Console.WriteLine("Screenshot skipped: " + ex.Message); }
    F<Avalonia.Controls.Primitives.ToggleButton>("MatchCaseToggle").IsChecked = false;
    findInput.Focus();
    var lastMatch = Cur();
    KeyOn(findInput, Key.Escape);
    Require(!bar.IsVisible && ed.IsFocused, "Esc in the find field closes the bar and focuses the editor");
    Require((ed.SelectionStart, ed.SelectionEnd) == lastMatch, "Closing leaves the current match selected in the editor");
    doc.SavedText = doc.Text;
    foreach (var d in tabs4.Items.OfType<NoteDocument>()) d.SavedText = d.Text;
    w4.Close();
}
var sessionRoot = Path.Combine(Path.GetTempPath(), $"mdhub-session-{Guid.NewGuid():N}");
try
{
    var sessionFile = Path.Combine(sessionRoot, "appdata", "mdhub", "session.json");
    Mdhub.Services.SessionManager.PathOverride = sessionFile;
    Mdhub.Services.SessionManager.Enabled = true;
    Mdhub.Services.SessionManager.Reset();
    var folderA = Directory.CreateDirectory(Path.Combine(sessionRoot, "Alpha")).FullName;
    var folderB = Directory.CreateDirectory(Path.Combine(sessionRoot, "Beta")).FullName;
    var a1 = Path.Combine(folderA, "one.md"); File.WriteAllText(a1, "one");
    var a2 = Path.Combine(folderA, "two.md"); File.WriteAllText(a2, "two");
    var b1 = Path.Combine(folderB, "three.md"); File.WriteAllText(b1, "three");
    var sflags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    T S<T>(Window w, string name) where T : Control => w.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    MainView ViewOf(Window w) => w.FindControl<MainView>("MainView")!;
    void Invoke(Window w, string method, params object[] args) { var task = (Task)typeof(MainView).GetMethod(method, sflags)!.Invoke(ViewOf(w), args)!; while (!task.IsCompleted) Pump(); task.GetAwaiter().GetResult(); }
    List<NoteDocument> TabsOf(Window w) => S<ListBox>(w, "Tabs").Items.OfType<NoteDocument>().ToList();

    MainWindow OpenAlpha()
    {
        var w = new MainWindow { Width = 900, Height = 640 };
        w.Show(); Pump();
        Invoke(w, "LoadRootAsync", folderA);
        Invoke(w, "OpenPathAsync", a1);
        Invoke(w, "OpenPathAsync", a2);
        S<ListBox>(w, "Tabs").SelectedItem = TabsOf(w).Single(d => d.Path == a1);
        Pump();
        return w;
    }
    var wa = OpenAlpha();
    var wb = new MainWindow { Width = 800, Height = 500 };
    wb.Show(); Pump();
    Invoke(wb, "LoadRootAsync", folderB);
    Invoke(wb, "OpenPathAsync", b1);
    wb.WindowState = WindowState.Maximized;
    Pump();
    TabsOf(wb).First(d => d.Path is null).Editor.Text = "unsaved scratch";
    Pump();
    Require(!File.Exists(sessionFile) || Mdhub.Services.SessionStore.Load(sessionFile) is { Windows.Count: 0 }, "No window session is written while windows are open (only Open Recent)");

    wa.Close(); Pump();
    var afterFirst = Mdhub.Services.SessionStore.Load(sessionFile);
    Require(afterFirst is { Windows.Count: 1 } && afterFirst.Windows[0].Folder == folderB,
        "Closing a window while others stay open removes it from the session and keeps the open ones (safe if killed)");
    var savedText = File.ReadAllText(sessionFile);
    wb.Close(); Pump();
    Require(wb.IsVisible && wb.OwnedWindows.Count == 1, "Closing a window with unsaved text still prompts");
    wb.OwnedWindows.Single().GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump();
    Require(wb.IsVisible && File.ReadAllText(sessionFile) == savedText, "A cancelled close does not change the saved session");
    wa = OpenAlpha();
    Mdhub.Services.SessionManager.BeginShutdown();
    var atShutdown = File.ReadAllText(sessionFile);
    wa.Close(); Pump();
    wb.Close(); Pump();
    wb.OwnedWindows.Single().GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Discard")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump();
    var final = Mdhub.Services.SessionStore.Load(sessionFile)!;
    var fa = final.Windows.FirstOrDefault(w => w.Folder == folderA);
    var fb = final.Windows.FirstOrDefault(w => w.Folder == folderB);
    Require(!wb.IsVisible && File.ReadAllText(sessionFile) == atShutdown && final.Windows.Count == 2 && fa is not null && fb is not null
        && fa.Tabs.SequenceEqual([a1, a2]) && fa.ActiveTab == 0 && fa.Width == 900 && fa.Height == 640
        && fb.Tabs.SequenceEqual([b1]) && fb.State == "Maximized",
        "Shutdown saves every window open at that point (folder, tabs, active tab, size, state); untitled buffers are not saved");
    Require(!File.ReadAllText(sessionFile).Contains("unsaved scratch"), "Unsaved buffer text is never written to the session");

    var restored = Mdhub.Services.SessionManager.Restore();
    foreach (var w in restored) w.Show();
    var until = DateTime.UtcNow.AddSeconds(10);
    while ((restored.Count < 2 || restored.Any(w => TabsOf(w).All(d => d.Path is null)) || TabsOf(restored.First(w => TabsOf(w).Any(d => d.Path == a1))).Count(d => d.Path is not null) < 2) && DateTime.UtcNow < until) Pump();
    Require(restored.Count == 2, "Each saved window is restored");
    restored = restored.OrderBy(w => TabsOf(w).Any(d => d.Path == a1) ? 0 : 1).ToList();
    Require(S<TextBlock>(restored[0], "FolderName").Text == "Alpha" && TabsOf(restored[0]).Select(d => d.Path).SequenceEqual([a1, a2])
        && ((NoteDocument)S<ListBox>(restored[0], "Tabs").SelectedItem!).Path == a1 && restored[0].Width == 900 && restored[0].Height == 640,
        "Restore reopens the folder, tabs (without the empty placeholder) and active tab, with the window size");
    Require(S<TextBlock>(restored[1], "FolderName").Text == "Beta" && TabsOf(restored[1]).Select(d => d.Path).SequenceEqual([b1]) && restored[1].WindowState == WindowState.Maximized,
        "The second window restores its own folder, tab and maximized state");
    restored[0].Close(); Pump();
    restored[1].Close(); Pump();
    var lastOnly = Mdhub.Services.SessionStore.Load(sessionFile);
    Require(lastOnly is { Windows.Count: 1 } && lastOnly.Windows[0].Folder == folderB, "Closing the last window saves just that window");
    Mdhub.Services.SessionStore.Save(sessionFile, final);

    File.Delete(a2);
    Directory.Delete(folderB, true);
    restored = Mdhub.Services.SessionManager.Restore();
    foreach (var w in restored) w.Show();
    until = DateTime.UtcNow.AddSeconds(10);
    while (restored.Count > 0 && TabsOf(restored[0]).All(d => d.Path is null) && DateTime.UtcNow < until) Pump();
    Pump();
    Require(restored.Count == 1 && TabsOf(restored[0]).Select(d => d.Path).SequenceEqual([a1]) && ((NoteDocument)S<ListBox>(restored[0], "Tabs").SelectedItem!).Path == a1,
        "Missing files and folders are skipped; a window with nothing left is not restored");
    var note = S<TextBlock>(restored[0], "Status").Text;
    Require(note == "Skipped 3 missing items from the last session.", "Status bar notes the skipped items: " + note);
    foreach (var w in restored) w.Close();
    Pump();

    File.WriteAllText(sessionFile, "{ not json");
    Require(Mdhub.Services.SessionManager.Restore().Count == 0, "A corrupt session file is ignored (start as today)");
    File.WriteAllText(sessionFile, "{\"Version\":99,\"Windows\":[{\"Folder\":\"C:\\\\\"}]}");
    Require(Mdhub.Services.SessionManager.Restore().Count == 0, "A wrong-schema session file is ignored");
    File.Delete(sessionFile);
    Require(Mdhub.Services.SessionManager.Restore().Count == 0, "A missing session file starts as today");
    Directory.Delete(folderA, true);
    File.WriteAllText(sessionFile, Mdhub.Services.SessionStore.Serialize(final));
    Require(Mdhub.Services.SessionManager.Restore().Count == 0, "A session with nothing valid left starts as today");
}
finally
{
    Mdhub.Services.SessionManager.Enabled = false;
    Mdhub.Services.SessionManager.PathOverride = null;
    Mdhub.Services.SessionManager.Reset();
    Directory.Delete(sessionRoot, recursive: true);
}
{
    var hw = new MainWindow { Width = 1100, Height = 600 };
    hw.Show(); Pump();
    var hflags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
    var md = hw.GetVisualDescendants().OfType<MdEditor>().First();
    TP P<TP>(string name) => (TP)typeof(MdEditor).GetProperty(name, hflags)!.GetValue(md)!;
    var doc = hw.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs").Items.OfType<NoteDocument>().First();
    var ed = doc.Editor;
    var source = "# Title\n\nhello **bold** world\n\n| a | b |\n|---|---|\n| x | y |\n\n"
        + string.Concat(Enumerable.Range(1, 80).Select(i => $"para {i}\n\n")) + "last target\n";
    ed.Text = source;
    Pump(); Pump();
    var hscroll = hw.GetVisualDescendants().OfType<ScrollViewer>().First(c => c.Name == "PreviewScroll");
    void Select(string text, int extra = 0) { var at = source.IndexOf(text, StringComparison.Ordinal); ed.SelectionStart = at; ed.SelectionEnd = at + text.Length + extra; }
    string Lit() => string.Concat(P<IEnumerable<Avalonia.Controls.Documents.Run>>("HighlightRuns").Select(r => r.Text));

    var updates = P<int>("HighlightUpdates");
    Select("hello");
    Dispatcher.UIThread.RunJobs();
    Require(P<int>("HighlightUpdates") == updates && P<int>("HighlightedRuns") == 0, "The preview highlight waits for the selection debounce");
    Pump();
    Require(Lit() == "hello" && P<IReadOnlyList<Control>>("HighlightedBlocks").Count == 0
        && P<IEnumerable<Avalonia.Controls.Documents.Run>>("HighlightRuns").All(r => r.Background == typeof(MdEditor).GetField("HighlightBrush", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)),
        "Selecting plain text highlights exactly that text in the preview");
    Require(P<int>("PreviewScrolls") == 0 && hscroll.Offset.Y == 0, "A visible highlight does not scroll the preview");

    updates = P<int>("HighlightUpdates");
    Select("world"); Select("bold"); Select("hel");
    Pump();
    Require(P<int>("HighlightUpdates") == updates + 1 && Lit() == "hel", "Rapid selection changes are coalesced into one update");
    Select("bold"); Pump();
    Require(Lit() == "bold", "Text inside emphasis maps exactly");
    Select("**bold**"); Pump();
    var blocks = P<IReadOnlyList<Control>>("HighlightedBlocks");
    Require(P<int>("HighlightedRuns") == 0 && blocks.Count == 1 && blocks[0] is TextBlock para && para.Inlines!.Text == "hello bold world",
        "A selection including markup highlights the whole paragraph");
    Select("a | b"); Pump();
    blocks = P<IReadOnlyList<Control>>("HighlightedBlocks");
    Require(blocks.Count == 1 && blocks[0] is Grid && P<int>("HighlightedRuns") == 0, "A partial table selection highlights the whole table");
    Select("x"); Pump();
    Require(Lit() == "x" && P<IReadOnlyList<Control>>("HighlightedBlocks").Count == 0, "Text inside one table cell maps exactly");
    Select("ello"); Pump();
    ed.SelectionEnd = ed.SelectionStart; Pump();
    Require(P<int>("HighlightedRuns") == 0 && P<IReadOnlyList<Control>>("HighlightedBlocks").Count == 0
        && hw.GetVisualDescendants().OfType<TextBlock>().Any(b => b.Inlines?.Text == "hello bold world"),
        "An empty selection clears the highlight and restores the preview text");

    Select("last target"); Pump(); Pump();
    var offsetAfter = hscroll.Offset.Y;
    Require(Lit() == "last target" && P<int>("PreviewScrolls") == 1 && offsetAfter > 0, "An offscreen highlight scrolls the preview to it");
    Select("para 80"); Pump(); Pump();
    Require(P<int>("PreviewScrolls") == 1 && hscroll.Offset.Y == offsetAfter, "A highlight already on screen does not scroll");
    Select("hello"); Pump(); Pump();
    Require(P<int>("PreviewScrolls") == 2 && hscroll.Offset.Y < offsetAfter, "Highlighting text above the view scrolls back up");
    ed.Text = "plain edit"; Pump(); Pump();
    Require(Lit() == (ed.SelectedText ?? ""), "Editing the text re-renders and the highlight follows the current selection, with nothing stale");
    ed.Text = "";
    hw.Close(); Pump();
}
{
    var tw = new MainWindow { Width = 1000, Height = 600 };
    tw.Show(); Pump();
    var strip = tw.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs");
    List<NoteDocument> Docs() => strip.Items.OfType<NoteDocument>().ToList();
    void DoubleClick(Point p)
    {
        for (var i = 0; i < 2; i++) { tw.MouseDown(p, MouseButton.Left); tw.MouseUp(p, MouseButton.Left); }
        Pump();
    }
    Point Center(Visual v) => v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), tw)!.Value;
    var before = Docs().Count;
    var blank = strip.TranslatePoint(new Point(strip.Bounds.Width - 20, strip.Bounds.Height / 2), tw)!.Value;
    Require(tw.InputHitTest(blank) is not null, "The blank tab strip area receives hits");
    DoubleClick(blank);
    var after = Docs();
    Require(after.Count == before + 1 && after[^1].Path is null && strip.SelectedItem == after[^1], "Double-clicking blank tab-strip space adds and selects an untitled note");
    var item = strip.GetVisualDescendants().OfType<ListBoxItem>().First();
    var titleText = item.GetVisualDescendants().OfType<TextBlock>().First(b => b.Text == ((NoteDocument)item.DataContext!).Title);
    DoubleClick(Center(titleText));
    Require(Docs().Count == before + 1, "Double-clicking an existing tab adds nothing");
    var close = strip.GetVisualDescendants().OfType<ListBoxItem>().Last().GetVisualDescendants().OfType<Button>().First();
    DoubleClick(Center(close));
    Require(Docs().Count <= before + 1 && Docs().Count >= 1, "Double-clicking a tab close button adds no new note");
    tw.Close(); Pump();
}
{
    var zflags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
    typeof(MainView).GetProperty("TransientStatusDuration", zflags)!.SetValue(null, TimeSpan.FromMilliseconds(300));
    var zw = new MainWindow { Width = 1000, Height = 700 };
    zw.Show(); Pump();
    var host = zw.GetVisualDescendants().OfType<LayoutTransformControl>().First(c => c.Name == "ZoomHost");
    string StatusOf(Window w) => w.GetVisualDescendants().OfType<TextBlock>().First(c => c.Name == "Status").Text ?? "";
    void Key(Window w, Key key, RawInputModifiers mods = RawInputModifiers.Control) { w.KeyPress(key, mods, PhysicalKey.None, null); w.KeyRelease(key, mods, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs(); }
    double ScaleOf(Window w) => w.GetVisualDescendants().OfType<LayoutTransformControl>().First(c => c.Name == "ZoomHost").LayoutTransform!.Value.M11;
    Require(zw.ZoomPercent == 100 && Math.Abs(ScaleOf(zw) - 1) < 1e-9, "Windows start at 100% zoom");
    Key(zw, Avalonia.Input.Key.OemPlus);
    Require(zw.ZoomPercent == 110, "Ctrl+= zooms in");
    Key(zw, Avalonia.Input.Key.OemPlus, RawInputModifiers.Control | RawInputModifiers.Shift);
    Key(zw, Avalonia.Input.Key.Add);
    Require(zw.ZoomPercent == 130, "Ctrl++ and numpad + zoom in");
    Key(zw, Avalonia.Input.Key.OemMinus); Key(zw, Avalonia.Input.Key.Subtract);
    Require(zw.ZoomPercent == 110, "Ctrl+- and numpad - zoom out");
    Key(zw, Avalonia.Input.Key.OemPlus, RawInputModifiers.None);
    Key(zw, Avalonia.Input.Key.OemPlus, RawInputModifiers.Control | RawInputModifiers.Alt);
    Require(zw.ZoomPercent == 110, "Without Ctrl (or with Alt) the keys do not zoom");
    Key(zw, Avalonia.Input.Key.D0);
    Require(zw.ZoomPercent == 100, "Ctrl+0 resets the zoom");
    Key(zw, Avalonia.Input.Key.Add); Key(zw, Avalonia.Input.Key.NumPad0);
    Require(zw.ZoomPercent == 100, "Ctrl+numpad 0 resets the zoom");
    for (var i = 0; i < 25; i++) Key(zw, Avalonia.Input.Key.OemPlus);
    Require(zw.ZoomPercent == 200, "Zoom stops at 200%");
    for (var i = 0; i < 25; i++) Key(zw, Avalonia.Input.Key.OemMinus);
    Require(zw.ZoomPercent == 50, "Zoom stops at 50%");
    Key(zw, Avalonia.Input.Key.D0);
    Key(zw, Avalonia.Input.Key.OemPlus); Key(zw, Avalonia.Input.Key.OemPlus);
    Require(StatusOf(zw).Contains("Zoom 120%"), "The status bar briefly shows the zoom level");
    Pump();
    var zmv = zw.GetVisualDescendants().OfType<MainView>().First();
    Require(Math.Abs(ScaleOf(zw) - 1.2) < 1e-9 && host.LayoutTransform is Avalonia.Media.ScaleTransform && Math.Abs(zmv.Bounds.Width * 1.2 - host.Bounds.Width) < 1.5,
        "The zoom is one layout transform around the window content (content laid out at 1/1.2 size)");
    Pump();
    Require(!StatusOf(zw).Contains("Zoom"), "The zoom status message clears itself");

    var zdoc = zw.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs").Items.OfType<NoteDocument>().First();
    zdoc.Editor.Text = string.Concat(Enumerable.Range(1, 300).Select(i => $"line {i}\n\n"));
    Pump(); Pump();
    var edScroll = zdoc.Editor.GetVisualDescendants().OfType<ScrollViewer>().First();
    var pvScroll = zw.GetVisualDescendants().OfType<ScrollViewer>().First(c => c.Name == "PreviewScroll");
    Point Mid(Visual v) => v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), zw)!.Value;
    var edMid = Mid(edScroll); var pvMid = Mid(pvScroll);
    zw.MouseWheel(edMid, new Vector(0, 1), RawInputModifiers.Control); Pump();
    Require(zw.ZoomPercent == 130 && edScroll.Offset.Y == 0, "Ctrl+wheel up over the editor zooms in without scrolling it");
    zw.MouseWheel(Mid(pvScroll), new Vector(0, -1), RawInputModifiers.Control); Pump();
    Require(zw.ZoomPercent == 120 && pvScroll.Offset.Y == 0, "Ctrl+wheel down over the preview zooms out without scrolling it");
    zw.MouseWheel(Mid(edScroll), new Vector(0, -1), RawInputModifiers.None); Pump();
    Require(zw.ZoomPercent == 120 && edScroll.Offset.Y > 0, "A plain wheel still scrolls the editor and does not zoom");

    var treeMenu = zw.GetVisualDescendants().OfType<TreeView>().First(c => c.Name == "FolderTree").ContextMenu!;
    treeMenu.Open(zw.GetVisualDescendants().OfType<TreeView>().First(c => c.Name == "FolderTree")); Pump();
    // Popups live in a PopupRoot window on desktop, or an OverlayPopupHost (headless); both carry the inherited Transform.
    double? PopupScale(Visual v) => v.GetSelfAndVisualAncestors().Append(TopLevel.GetTopLevel(v)).OfType<Visual>().Where(a => a is Avalonia.Controls.Primitives.PopupRoot || a.GetType().Name == "OverlayPopupHost")
        .Select(a => (a.GetType().GetProperty("Transform")?.GetValue(a) as Avalonia.Media.Transform)?.Value.M11).FirstOrDefault(m => m is not null);
    Require(PopupScale(treeMenu) is { } pt && Math.Abs(pt - 1.2) < 1e-6, "Context menus (separate popup roots) are scaled by the window zoom");
    treeMenu.Close(); Pump();
    var fileMenu = zw.GetVisualDescendants().OfType<Menu>().First(c => c.Name == "MainMenu").Items.OfType<MenuItem>().First();
    fileMenu.Open(); Pump();
    var zsub = fileMenu.Items.OfType<MenuItem>().First();
    Require(PopupScale(zsub) is { } mt && Math.Abs(mt - 1.2) < 1e-6, "Menu dropdowns are scaled by the window zoom");
    fileMenu.Close(); Pump();
    var findTe = zw.GetVisualDescendants().OfType<TextEditor>().First();
    findTe.OpenFind(false); Pump();
    var findBar = zw.GetVisualDescendants().OfType<FindBar>().First();
    Require(findBar.IsVisible && findBar.GetVisualAncestors().Contains(host), "The find bar is inside the zoomed content");
    findTe.CloseFind(); Pump();

    var zv = new MainWindow { Width = 900, Height = 600 };
    zv.Show(); Pump();
    Key(zv, Avalonia.Input.Key.OemMinus); Pump();
    Require(zv.ZoomPercent == 90 && zw.ZoomPercent == 120 && Math.Abs(ScaleOf(zw) - 1.2) < 1e-9 && Math.Abs(ScaleOf(zv) - 0.9) < 1e-9, "Each window has its own zoom level");
    Require(zw.CaptureSession().Zoom == 120 && zv.CaptureSession().Zoom == 90, "Each window's zoom is captured for the session");

    var zroot = Path.Combine(Path.GetTempPath(), $"mdhub-zoom-{Guid.NewGuid():N}");
    var zfolder = Directory.CreateDirectory(Path.Combine(zroot, "Notes")).FullName;
    var zfile = Path.Combine(zroot, "session.json");
    Mdhub.Services.SessionStore.Save(zfile, new Mdhub.Services.SessionData { Windows = [zw.CaptureSession() with { Folder = zfolder, Tabs = [] }, zv.CaptureSession() with { Folder = zfolder, Tabs = [] }] });
    File.WriteAllText(Path.Combine(zroot, "old.json"), $"{{\"Version\":1,\"Windows\":[{{\"Folder\":{System.Text.Json.JsonSerializer.Serialize(zfolder)},\"Tabs\":[]}}]}}");
    zdoc.Editor.Text = "";
    zw.Close(); zv.Close(); Pump();
    Mdhub.Services.SessionManager.PathOverride = zfile;
    var zr = Mdhub.Services.SessionManager.Restore();
    foreach (var w in zr) w.Show();
    Pump();
    Require(zr.Count == 2 && zr[0].ZoomPercent == 120 && zr[1].ZoomPercent == 90 && Math.Abs(ScaleOf(zr[0]) - 1.2) < 1e-9 && Math.Abs(ScaleOf(zr[1]) - 0.9) < 1e-9,
        "Session restore brings back each window's zoom");
    Require(!StatusOf(zr[0]).Contains("Zoom"), "Restoring the zoom does not show the zoom message");
    foreach (var w in zr) w.Close();
    Pump();
    Mdhub.Services.SessionManager.PathOverride = Path.Combine(zroot, "old.json");
    zr = Mdhub.Services.SessionManager.Restore();
    Require(zr.Count == 1 && zr[0].ZoomPercent == 100, "An old session without zoom restores at 100%");
    Mdhub.Services.SessionManager.PathOverride = null;
    Directory.Delete(zroot, true);
}
{
    var lw = new MainWindow { Width = 1100, Height = 700 };
    lw.Show(); Pump();
    var lflags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var lmd = lw.GetVisualDescendants().OfType<MdEditor>().First();
    Control? CurBlock() => (Control?)typeof(MdEditor).GetProperty("CurrentLineBlock", lflags)!.GetValue(lmd);
    var ldoc = lw.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs").Items.OfType<NoteDocument>().First();
    var led = ldoc.Editor;
    var band = lw.GetVisualDescendants().OfType<CurrentLineOverlay>().First();
    var lsrc = "alpha\nbravo\ncharlie\n\n# Head\n\npara one\n\n| a |\n|---|\n| b |\n";
    led.Text = lsrc; Pump(); Pump();
    var pres = led.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().First();
    double ExpectedTop(int index) => pres.TranslatePoint(new Point(0, pres.TextLayout.HitTestTextPosition(index).Top), band)!.Value.Y;
    led.CaretIndex = lsrc.IndexOf("bravo") + 2; Pump();
    var r1 = band.LineRect();
    var rowH = pres.TextLayout.HitTestTextPosition(0).Height;
    Require(r1 is { } b1 && Math.Abs(b1.Y - ExpectedTop(lsrc.IndexOf("bravo"))) < 0.5 && Math.Abs(b1.Height - rowH) < 0.5 && b1.Width == band.Bounds.Width,
        "The current-line band covers the caret's line across the editor width");
    var zIndex = ((Panel)band.Parent!).Children.IndexOf(band);
    Require(zIndex < ((Panel)band.Parent!).Children.IndexOf(lw.GetVisualDescendants().OfType<ContentControl>().First(c => c.Name == "EditorHost"))
        && zIndex < ((Panel)band.Parent!).Children.IndexOf(lw.GetVisualDescendants().OfType<FindHighlightOverlay>().First()), "The band is drawn behind the text and the find highlights");
    led.CaretIndex = 1; Pump();
    Require(band.LineRect() is { } b2 && b2.Y < r1!.Value.Y && Math.Abs(b2.Y - ExpectedTop(0)) < 0.5, "Moving the caret moves the band");
    led.SelectionStart = 0; led.SelectionEnd = 3; Pump();
    Require(band.LineRect() is null && CurBlock() is null, "A selection hides the editor band and the preview block highlight");
    led.SelectionStart = led.SelectionEnd = lsrc.IndexOf("para one") + 1; Pump();
    Require(band.LineRect() is not null && CurBlock() is TextBlock { } pb && pb.Inlines!.Text == "para one", "The preview highlights the block for the caret's line");
    led.CaretIndex = lsrc.IndexOf("| b |") + 2; Pump();
    Require(CurBlock() is Grid, "A caret inside a table highlights the whole table");
    led.CaretIndex = lsrc.IndexOf("\n\n# Head") + 1; Pump();
    Require(CurBlock() is null && band.LineRect() is not null, "A blank line has no preview block but still shows the editor band");
    led.CaretIndex = lsrc.IndexOf("Head"); Pump();
    Require(CurBlock() is TextBlock { FontSize: 28 }, "Caret moves update the preview block");
    led.CaretIndex = lsrc.IndexOf("# Head"); Pump();
    var beforeType = band.LineRect()!.Value.Y;
    led.SelectedText = "new line\n"; Pump(); Pump();
    Require(band.LineRect() is { } b3 && b3.Y > beforeType && Math.Abs(b3.Y - ExpectedTop(led.CaretIndex)) < 0.5 && CurBlock() is TextBlock { FontSize: 28 },
        "Typing a line break moves the band to the new line and the preview follows");
    led.Text = "short\n" + string.Join(" ", Enumerable.Repeat("wrapping", 120)) + "\nend";
    led.TextWrapping = Avalonia.Media.TextWrapping.Wrap; Pump(); Pump();
    pres = led.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().First();
    led.CaretIndex = 20; Pump();
    Require(band.LineRect() is { } bw && bw.Height > rowH * 2.5 && Math.Abs(bw.Y - ExpectedTop(6)) < 0.5, "With wrap on, the band covers every visual row of the logical line");
    lw.SetZoom(150, announce: false); Pump(); Pump();
    Require(band.LineRect() is { } bz && Math.Abs(bz.Y - ExpectedTop(6)) < 0.5 && bz.Height > rowH * 2.5, "The band stays on the caret line after a zoom change");
    lw.SetZoom(100, announce: false);
    led.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;

    // Dialogs follow the owner's zoom.
    lw.SetZoom(150, announce: false); Pump();
    var dlgTask = Mdhub.Services.DialogService.ShowAsync(lw, "Confirm", "Really?", "OK", "Cancel");
    Pump();
    var dlg = lw.OwnedWindows.Single();
    Require(dlg.Content is LayoutTransformControl { LayoutTransform: { } dz } && Math.Abs(dz.Value.M11 - 1.5) < 1e-9 && dlg.Width == 720, "A confirm dialog scales its content and width with the owner's zoom");
    dlg.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    var nameTask = Mdhub.Services.DialogService.PromptNameAsync(lw, "New note", Path.GetTempPath(), "note.md");
    Pump();
    dlg = lw.OwnedWindows.Single();
    Require(dlg.Content is LayoutTransformControl && dlg.Width == 720, "The name prompt follows the owner's zoom");
    dlg.Close(); Pump();
    lw.SetZoom(100, announce: false); Pump();
    dlgTask = Mdhub.Services.DialogService.ShowAsync(lw, "Confirm", "Really?", "OK");
    Pump();
    dlg = lw.OwnedWindows.Single();
    Require(dlg.Content is not LayoutTransformControl && dlg.Width == 480, "At 100% dialogs are unchanged");
    dlg.Close(); Pump();
    led.Text = "";
    lw.Close(); Pump();
}
{
    var sw = new MainWindow { Width = 1100, Height = 700 };
    sw.Show(); Pump();
    var smenu = sw.GetVisualDescendants().OfType<Menu>().First(c => c.Name == "MainMenu");
    var stops = smenu.Items.OfType<MenuItem>().ToList();
    Require(stops.Select(m => m.Header as string).SequenceEqual(["_File", "_Edit", "_View", "_Settings", "_Help"]), "Settings sits between View and Help");
    var settings = stops[3];
    Require(settings.Items.OfType<MenuItem>().Select(m => m.Header as string).SequenceEqual(["Shortcut", "_Theme", "_Language"]), "Settings contains Shortcut, Theme and Language");
    MenuItem Item(int top, string header) => stops[top].Items.OfType<MenuItem>().First(m => (m.Header as string)!.Replace("_", "") == header);
    var expected = new (int, string, string)[] { (0, "New", "Ctrl+N"), (0, "Open File…", "Ctrl+O"), (0, "Open Folder…", "Ctrl+Shift+O"), (0, "Save", "Ctrl+S"), (0, "Save As…", "Ctrl+Shift+S"),
        (0, "Close Tab", "Ctrl+W"), (1, "Undo", "Ctrl+Z"), (1, "Redo", "Ctrl+Y"), (1, "Cut", "Ctrl+X"), (1, "Copy", "Ctrl+C"), (1, "Paste", "Ctrl+V"), (1, "Select All", "Ctrl+A"), (2, "Toggle Word Wrap", "Alt+Z") };
    Require(expected.All(x => Item(x.Item1, x.Item2).InputGesture?.ToString() == x.Item3), "Menu items show their shortcuts from the table");
    Require(Item(0, "Exit").InputGesture is null && Item(4, "About mdhub").InputGesture is null, "Items without a shortcut show none");
    stops[0].Open(); Pump();
    var gestureText = Item(0, "Save").GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(b => b.Name == "PART_InputGestureText");
    Require(gestureText is { Text: "Ctrl+S", Foreground: Avalonia.Media.ISolidColorBrush { Color: var gc } } && gc == Avalonia.Media.Color.Parse("#7D8796") && gestureText.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right,
        "The shortcut text is drawn right-aligned in a muted color");
    var headerPresenter = Item(0, "Save").GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == "PART_HeaderPresenter");
    Require(Item(0, "Save").Padding.Left == 19 && stops[0].Padding.Left != 19, "Dropdown items get 8px more left padding (top-level headers unchanged)");
    stops[0].Close(); Pump();

    var sflags2 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var sview = sw.GetVisualDescendants().OfType<MainView>().First();
    var fired = new List<string>();
    typeof(MainView).GetEvent("ShortcutFired", sflags2)!.GetAddMethod(true)!.Invoke(sview, [new Action<string>(fired.Add)]);
    List<NoteDocument> STabs() => sw.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs").Items.OfType<NoteDocument>().ToList();
    var sdoc = STabs()[0];
    sdoc.Editor.Focus(); Pump();
    void SKey(Key key, RawInputModifiers mods) { sw.KeyPress(key, mods, PhysicalKey.None, null); sw.KeyRelease(key, mods, PhysicalKey.None, null); Pump(); }
    var tabCount = STabs().Count;
    SKey(Key.N, RawInputModifiers.Control);
    Require(fired.SequenceEqual(["file.new"]) && STabs().Count == tabCount + 1, "Ctrl+N fires once and adds one note");
    fired.Clear();
    var wrapBefore = STabs().Last().Editor.TextWrapping;
    SKey(Key.Z, RawInputModifiers.Alt);
    Require(fired.SequenceEqual(["view.wordWrap"]) && STabs().Last().Editor.TextWrapping != wrapBefore, "Alt+Z toggles word wrap once");
    fired.Clear();
    SKey(Key.Z, RawInputModifiers.Alt);
    Require(STabs().Last().Editor.TextWrapping == wrapBefore, "A second Alt+Z toggles it back (no double toggle)");
    fired.Clear();
    SKey(Key.F, RawInputModifiers.Control);
    Require(fired.SequenceEqual(["find.find"]) && sw.GetVisualDescendants().OfType<TextEditor>().First().IsFindOpen, "Ctrl+F fires once and opens the find bar");
    fired.Clear();
    SKey(Key.Escape, RawInputModifiers.None);
    Require(!sw.GetVisualDescendants().OfType<TextEditor>().First().IsFindOpen && fired.Count == 0, "Esc closes the find bar");
    var zoomBefore = sw.ZoomPercent;
    SKey(Key.OemPlus, RawInputModifiers.Control);
    Require(sw.ZoomPercent == zoomBefore + 10 && fired.Count == 0, "Ctrl+= zooms exactly one step and is not handled twice");
    SKey(Key.D0, RawInputModifiers.Control);
    var ed2 = STabs().Last().Editor;
    ed2.Focus(); ed2.Text = ""; Pump();
    ed2.Text = "abc"; ed2.CaretIndex = 3; Pump();
    sw.KeyTextInput("d"); Pump();
    SKey(Key.Z, RawInputModifiers.Control);
    Require(fired.Count == 0 && ed2.Text == "abc", "Ctrl+Z is left to the editor and undoes once");

    sw.SetZoom(120, announce: false); Pump();
    settings.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
    var sdlg = sw.OwnedWindows.Single();
    var rows = sdlg.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("shortcut-row")).ToList();
    var areas = sdlg.GetVisualDescendants().OfType<TextBlock>().Where(b => b.Classes.Contains("shortcut-area")).Select(b => b.Text).ToList();
    Require(sdlg.Title == "Shortcuts" && rows.Count == Mdhub.Services.Shortcuts.All.Count && areas.SequenceEqual(["File", "Edit", "View", "Find", "Zoom", "Editor"]),
        "The Shortcut dialog lists every shortcut grouped by area");
    var saveRow = rows.First(r => Equals(r.Tag, "file.saveAs"));
    Require(saveRow.Children.OfType<TextBlock>().Where(b => b.Name != "ShortcutCustomMark").Select(b => b.Text).SequenceEqual(["Save as", "Ctrl+Shift+S"]), "Dialog rows show the label and the gesture");
    Require(!sdlg.GetVisualDescendants().OfType<TextBox>().Any(), "The Shortcut dialog has no text fields (keys are recorded, not typed)");
    Require(sdlg.Content is LayoutTransformControl { LayoutTransform: { } sz } && Math.Abs(sz.Value.M11 - 1.2) < 1e-9 && sdlg.Width == 672 && sdlg.Height == 720, "The Shortcut dialog follows the owner window's zoom");
    sdlg.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Close")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
    Require(sw.OwnedWindows.Count == 0, "Close dismisses the Shortcut dialog");
    sw.SetZoom(100, announce: false);
    foreach (var d in STabs()) d.Editor.Text = "";
    sw.Close(); Pump();
}
{
    var ew = new MainWindow { Width = 1100, Height = 700 };
    ew.Show(); Pump();
    var eflags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var eview = ew.GetVisualDescendants().OfType<MainView>().First();
    var efired = new List<string>();
    typeof(MainView).GetEvent("ShortcutFired", eflags)!.GetAddMethod(true)!.Invoke(eview, [new Action<string>(efired.Add)]);
    var gotoLines = new List<int>();
    typeof(MainView).GetEvent("GoToLineCompleted", eflags)!.GetAddMethod(true)!.Invoke(eview, [new Action<int>(gotoLines.Add)]);
    var clockProp = typeof(MainView).GetProperty("Clock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    var savedClock = clockProp.GetValue(null);
    clockProp.SetValue(null, new Func<DateTime>(() => new DateTime(2026, 10, 29, 14, 5, 9)));
    const string Stamp = "2026-10-29 14:05:09";
    void EKey(Key key, RawInputModifiers mods) { ew.KeyPress(key, mods, PhysicalKey.None, null); ew.KeyRelease(key, mods, PhysicalKey.None, null); Pump(); }
    var etabs = ew.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs");
    var md = (NoteDocument)typeof(MainView).GetMethod("AddDocument", eflags)!.Invoke(eview, [null, "", "edit.md", null])!; Pump();
    var ed = md.Editor;
    string Expect(string text, int s, int e, string action)
    {
        var edit = Mdhub.Text.MarkdownFormatting.Apply(text, s, e, action);
        return text[..edit.Start] + edit.Replacement + text[edit.End..];
    }
    void Prep(TextBox box, string text, int s, int e) { box.Text = text; box.Focus(); box.SelectionStart = s; box.SelectionEnd = e; Pump(); efired.Clear(); }
    foreach (var (key, mods, id, action) in new[] { (Key.B, RawInputModifiers.Control, "editor.bold", "bold"), (Key.I, RawInputModifiers.Control, "editor.italic", "italic"),
        (Key.L, RawInputModifiers.Control, "editor.link", "link"), (Key.OemQuotes, RawInputModifiers.Control, "editor.quote", "quote"), (Key.OemQuotes, RawInputModifiers.Control | RawInputModifiers.Shift, "editor.quote", "quote") })
    {
        Prep(ed, "hello world", 6, 11);
        EKey(key, mods);
        Require(ed.Text == Expect("hello world", 6, 11, action) && ed.Text != "hello world" && efired.SequenceEqual([id]), $"{key}+{mods} applies {action} once, like the toolbar");
    }
    Prep(ed, "hello world", 6, 11);
    EKey(Key.OemQuotes, RawInputModifiers.Control | RawInputModifiers.Alt);
    Require(ed.Text == "hello world" && efired.Count == 0, "Ctrl+Alt+' is not the quote shortcut (exact modifiers)");

    Prep(ed, "abc XYZ end", 4, 7);
    EKey(Key.OemSemicolon, RawInputModifiers.Control);
    Require(ed.Text == $"abc {Stamp} end" && ed.CaretIndex == 4 + Stamp.Length && ed.SelectionStart == ed.SelectionEnd && efired.SequenceEqual(["editor.insertTime"]),
        "Ctrl+; replaces the selection with the local time and puts the caret after it");
    ed.Undo(); Pump();
    Require(ed.Text == "abc XYZ end", "One undo removes the inserted time and restores the selection text");
    ed.Redo(); Pump();
    Require(ed.Text == $"abc {Stamp} end", "Redo reinserts the time in one step");

    var txt = (NoteDocument)typeof(MainView).GetMethod("AddDocument", eflags)!.Invoke(eview, [null, "", "plain.txt", null])!; Pump();
    Require(!txt.IsMarkdown, "Plain text note created");
    var ted = txt.Editor;
    foreach (var (key, mods) in new[] { (Key.B, RawInputModifiers.Control), (Key.I, RawInputModifiers.Control), (Key.L, RawInputModifiers.Control), (Key.OemQuotes, RawInputModifiers.Control) })
    {
        Prep(ted, "hello world", 6, 11);
        EKey(key, mods);
        Require(ted.Text == "hello world" && efired.Count == 0, $"{key} does nothing in a plain text note");
    }
    Prep(ted, "a", 1, 1);
    EKey(Key.OemSemicolon, RawInputModifiers.Control);
    Require(ted.Text == "a" + Stamp && efired.SequenceEqual(["editor.insertTime"]), "Ctrl+; works in plain text");
    Prep(ted, "one\ntwo\nthree", 0, 0);
    EKey(Key.G, RawInputModifiers.Control);
    var tdlg = ew.OwnedWindows.SingleOrDefault();
    Require(tdlg is not null && efired.SequenceEqual(["editor.goToLine"]), "Ctrl+G works in plain text too");
    tdlg!.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); Pump();

    // Back to the Markdown note.
    etabs.SelectedItem = md; Pump();
    var sb = new System.Text.StringBuilder();
    for (var i = 1; i <= 120; i++) sb.Append($"Paragraph {i}\n\n");
    var gLongText = sb.ToString();
    var gLineCount = Mdhub.Text.EditorCommands.LineCount(gLongText);
    Prep(ed, gLongText, 0, 0);
    Pump(); Pump();

    // Focus scoping: away from the editor only Ctrl+G and Ctrl+Shift+G act.
    ew.GetVisualDescendants().OfType<Button>().First(b => b.Name == "TogglePreviewButton").Focus(); Pump();
    Require(!ed.IsKeyboardFocusWithin, "Focus moved off the editor");
    EKey(Key.B, RawInputModifiers.Control);
    EKey(Key.OemSemicolon, RawInputModifiers.Control);
    Require(ed.Text == gLongText && efired.Count == 0, "Formatting and insert time need the editor to have focus");
    EKey(Key.G, RawInputModifiers.Control | RawInputModifiers.Shift);
    Require(efired.SequenceEqual(["view.previewCaret"]), "Ctrl+Shift+G works with focus elsewhere in the window");
    efired.Clear();
    EKey(Key.G, RawInputModifiers.Control);
    Require(ew.OwnedWindows.Count == 1 && efired.SequenceEqual(["editor.goToLine"]), "Ctrl+G works with focus elsewhere in the window");
    ew.OwnedWindows[0].KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); Pump();

    // Find bar text fields: none of the new shortcuts fire.
    ed.Focus(); Pump();
    EKey(Key.F, RawInputModifiers.Control);
    var gFindInput = ew.GetVisualDescendants().OfType<TextBox>().First(b => b.Name == "FindInput");
    Require(gFindInput.IsKeyboardFocusWithin, "Find field has focus");
    gFindInput.Text = "Para"; Pump(); efired.Clear();
    foreach (var (key, mods) in new[] { (Key.B, RawInputModifiers.Control), (Key.I, RawInputModifiers.Control), (Key.L, RawInputModifiers.Control), (Key.OemQuotes, RawInputModifiers.Control),
        (Key.OemSemicolon, RawInputModifiers.Control), (Key.G, RawInputModifiers.Control), (Key.G, RawInputModifiers.Control | RawInputModifiers.Shift) })
        EKey(key, mods);
    Require(efired.Count == 0 && ew.OwnedWindows.Count == 0 && ed.Text == gLongText && gFindInput.Text == "Para", "No editor shortcut fires while a find field has focus");
    EKey(Key.Escape, RawInputModifiers.None);
    ed.Focus(); ed.SelectionStart = ed.SelectionEnd = 0; Pump(); efired.Clear();

    // Go to line prompt: digits only, gHint, zoom, clamp, Enter/Esc, scrolling.
    ew.SetZoom(130, announce: false); Pump();
    var gEditorScroll = ed.GetVisualDescendants().OfType<ScrollViewer>().First();
    var gPreviewScroll = ew.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "PreviewScroll");
    EKey(Key.G, RawInputModifiers.Control);
    var gdlg = ew.OwnedWindows.Single();
    var gHint = gdlg.GetVisualDescendants().OfType<TextBlock>().First(b => b.Name == "LineHint");
    var gLineBox = gdlg.GetVisualDescendants().OfType<TextBox>().First(b => b.Name == "LineNumber");
    Require(gdlg.Title == "Go to line" && gHint.Text == $"Line number (1 to {gLineCount})" && gLineBox.Text == "1", "Go to line shows a 1 to N gHint and the current line");
    Require(gdlg.Content is LayoutTransformControl { LayoutTransform: { } gz } && Math.Abs(gz.Value.M11 - 1.3) < 1e-9 && gdlg.Width == 390, "Go to line follows the window zoom");
    gLineBox.Text = ""; Pump();
    foreach (var ch in new[] { "4", "a", "-", "1" }) { gdlg.KeyTextInput(ch); Pump(); }
    Require(gLineBox.Text == "41", "Only digits can be typed");
    gLineBox.Text = "4x1"; Pump();
    Require(gLineBox.Text == "41", "Pasted non-digits are removed");
    gdlg.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null); Pump(); Pump();
    var gLine41 = Mdhub.Text.EditorCommands.LineStart(gLongText, 41);
    Require(ew.OwnedWindows.Count == 0 && ed.CaretIndex == gLine41 && ed.SelectionStart == ed.SelectionEnd && gotoLines.SequenceEqual([41]) && gLongText[gLine41..].StartsWith("Paragraph 21"),
        "Enter moves the caret to the start of the line");
    Require(gEditorScroll.Offset.Y > 0 && ed.IsKeyboardFocusWithin, "The editor scrolls to the line and keeps focus");
    bool Centered(string text)
    {
        var block = gPreviewScroll.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(b => (b.Inlines?.Count > 0 ? string.Concat(b.Inlines.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)) : b.Text) == text);
        if (block?.TranslatePoint(new Point(0, block.Bounds.Height / 2), gPreviewScroll) is not { } mid) { Console.WriteLine($"no block {text}"); return false; }
        var ok = Math.Abs(mid.Y - gPreviewScroll.Viewport.Height / 2) < 2;
        if (!ok) Console.WriteLine($"{text}: mid {mid.Y} viewport {gPreviewScroll.Viewport.Height} offset {gPreviewScroll.Offset.Y}");
        return ok;
    }
    Require(gPreviewScroll.Offset.Y > 0 && Centered("Paragraph 21"), "The preview scrolls to the matching block");

    EKey(Key.G, RawInputModifiers.Control);
    gdlg = ew.OwnedWindows.Single();
    gdlg.GetVisualDescendants().OfType<TextBox>().First(b => b.Name == "LineNumber").Text = "99999";
    gdlg.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null); Pump(); Pump();
    Require(gotoLines.Last() == gLineCount && ed.CaretIndex == gLongText.Length, "Out-of-range line numbers clamp to the last line");
    EKey(Key.G, RawInputModifiers.Control);
    gdlg = ew.OwnedWindows.Single();
    gdlg.GetVisualDescendants().OfType<TextBox>().First(b => b.Name == "LineNumber").Text = "0";
    gdlg.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null); Pump(); Pump();
    Require(gotoLines.Last() == 1 && ed.CaretIndex == 0, "Line 0 clamps to line 1");
    var gBefore = ed.CaretIndex; var gotoCount = gotoLines.Count;
    EKey(Key.G, RawInputModifiers.Control);
    gdlg = ew.OwnedWindows.Single();
    gdlg.GetVisualDescendants().OfType<TextBox>().First(b => b.Name == "LineNumber").Text = "77";
    gdlg.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); Pump();
    Require(ew.OwnedWindows.Count == 0 && ed.CaretIndex == gBefore && gotoLines.Count == gotoCount, "Esc cancels Go to line");
    ew.SetZoom(100, announce: false); Pump();

    // Ctrl+Shift+G: center the caret's block without moving the caret.
    var gLine101 = Mdhub.Text.EditorCommands.LineStart(gLongText, 101);
    ed.Focus(); ed.SelectionStart = ed.SelectionEnd = gLine101; ed.CaretIndex = gLine101; Pump();
    gPreviewScroll.Offset = new Vector(0, 0); Pump();
    efired.Clear();
    EKey(Key.G, RawInputModifiers.Control | RawInputModifiers.Shift); Pump();
    Require(efired.SequenceEqual(["view.previewCaret"]) && ed.CaretIndex == gLine101 && ed.SelectionStart == gLine101 && Centered("Paragraph 51"), "Ctrl+Shift+G centers the caret's block in the preview without moving the caret");
    ed.SelectionStart = ed.SelectionEnd = gLine101 + 1; ed.CaretIndex = gLine101 + 1; Pump(); // blank line below uses the block above
    gPreviewScroll.Offset = new Vector(0, 0); Pump();
    EKey(Key.G, RawInputModifiers.Control | RawInputModifiers.Shift); Pump();
    Require(Centered("Paragraph 51") || Centered("Paragraph 52"), "Ctrl+Shift+G on a blank line centers the nearest block");

    // Dialog and menu show the new shortcuts.
    var emenu = ew.GetVisualDescendants().OfType<Menu>().First(c => c.Name == "MainMenu");
    var etops = emenu.Items.OfType<MenuItem>().ToList();
    MenuItem EItem(int top, string header) => etops[top].Items.OfType<MenuItem>().First(m => (m.Header as string)!.Replace("_", "") == header);
    Require(new[] { (1, "Bold", "Ctrl+B"), (1, "Italic", "Ctrl+I"), (1, "Insert Link", "Ctrl+L"), (1, "Insert Date and Time", "Ctrl+OemSemicolon"), (1, "Go to Line…", "Ctrl+G"), (2, "Scroll Preview to Caret", "Ctrl+Shift+G") }
        .All(x => EItem(x.Item1, x.Item2).InputGesture?.ToString() == x.Item3 || x.Item2 == "Insert Date and Time" && EItem(1, x.Item2).InputGesture is { Key: Key.OemSemicolon }), "Menu items carry the new gestures");
    etops[1].Open(); Pump();
    string? MenuText(string header) => EItem(1, header).GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(b => b.Name == "PART_InputGestureText")?.Text;
    Require(MenuText("Block Quote") == "Ctrl+\"" && MenuText("Insert Date and Time") == "Ctrl+;" && MenuText("Bold") == "Ctrl+B" && MenuText("Redo") == "Ctrl+Y", "Menu shortcut text matches the table (Ctrl+\" and Ctrl+;)");
    etops[1].Close(); Pump();
    Prep(ed, "menu", 0, 4);
    EItem(1, "Bold").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
    Require(ed.Text == "**menu**", "Edit > Bold applies bold to the active note");
    ew.SetZoom(100, announce: false);
    etops[3].Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
    var sd = ew.OwnedWindows.Single();
    string RowText(string id) => string.Join("|", sd.GetVisualDescendants().OfType<Grid>().First(g => Equals(g.Tag, id)).Children.OfType<TextBlock>().Where(b => b.Name != "ShortcutCustomMark").Select(b => b.Text));
    Require(RowText("editor.quote") == "Block quote (Markdown)|Ctrl+\"" && RowText("editor.insertTime") == "Insert date and time|Ctrl+;" && RowText("editor.goToLine") == "Go to line|Ctrl+G"
        && RowText("view.previewCaret") == "Scroll preview to caret|Ctrl+Shift+G" && RowText("editor.bold") == "Bold (Markdown)|Ctrl+B", "The Shortcut dialog lists the new shortcuts");
    sd.Close(); Pump();

    clockProp.SetValue(null, savedClock);
    foreach (var d in etabs.Items.OfType<NoteDocument>()) d.Editor.Text = "";
    ew.Close(); Pump();
}
{
    // Structure signatures (real Markdig pipeline).
    var sig = Mdhub.Rendering.MarkdownStructure.SameAs("Hello **bold** world\n\n- a\n");
    Require(sig("Hello **bolder** world\n\n- a b\n") && sig("Hello \\* **bold** world\n\n- a\n"), "Text and escaped characters keep the Markdown structure");
    Require(!sig("Hello **bold** world\n\n- a\n- b\n") && !sig("Hello **bold ** world\n\n- a\n") && !sig("Hello **bold** <b>world\n\n- a\n") && !sig("- Hello **bold** world\n\n- a\n"),
        "New list items, broken emphasis, HTML and list starters change the structure");

    var pw = new MainWindow { Width = 1300, Height = 800 };
    pw.Show(); Pump();
    var pflags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var pview = pw.GetVisualDescendants().OfType<MainView>().First();
    var pmd = pw.GetVisualDescendants().OfType<MdEditor>().First();
    var pfired = new List<string>();
    typeof(MainView).GetEvent("ShortcutFired", pflags)!.GetAddMethod(true)!.Invoke(pview, [new Action<string>(pfired.Add)]);
    T PGet<T>(string name) => (T)typeof(MdEditor).GetProperty(name, pflags)!.GetValue(pmd)!;
    bool Begin(Control c) => (bool)typeof(MdEditor).GetMethod("BeginPreviewEdit", pflags)!.Invoke(pmd, [c])!;
    string? Commit() => (string?)typeof(MdEditor).GetMethod("CommitPreviewEdit", pflags)!.Invoke(pmd, [])!;
    TextBox? Box() => (TextBox?)typeof(MdEditor).GetProperty("PreviewEditBox", pflags)!.GetValue(pmd);
    string Flat(Avalonia.Controls.Documents.InlineCollection inl) => string.Concat(inl.Select(i => i switch { Avalonia.Controls.Documents.Run r => r.Text, Avalonia.Controls.Documents.Span s => Flat(s.Inlines), Avalonia.Controls.Documents.LineBreak => "\n", _ => "" }));
    string BlockText(Control c) => c is TextBlock { Inlines: { Count: > 0 } inl } ? Flat(inl) : (c as TextBlock)?.Text ?? "";
    Control Block(string text) => PGet<IEnumerable<Control>>("PreviewBlocks").First(c => BlockText(c) == text);
    void PKey(Window win, Key key, RawInputModifiers mods = RawInputModifiers.None) { win.KeyPress(key, mods, PhysicalKey.None, null); win.KeyRelease(key, mods, PhysicalKey.None, null); Pump(); }
    var pdoc = (NoteDocument)typeof(MainView).GetMethod("AddDocument", pflags)!.Invoke(pview, [null, "", "preview.md", null])!; Pump();
    var ped = pdoc.Editor;
    var start = "# Title\n\nHello **bold** world.\n\n- item one\n- item two\n\n> quoted text\n\n| A | B |\n| --- | --- |\n| cell | two |\n\nUse `a*b` here.\n\n```js\nlet x = 1;\n```\n";
    ped.Text = start; Pump(); Pump();
    pdoc.SavedText = start;
    Pump();
    Require(!pdoc.IsDirty, "Preview note starts clean");
    string? EditTo(string blockText, string newText, bool enter = true)
    {
        Require(Begin(Block(blockText)), $"Preview block '{blockText}' can be edited");
        var box = Box()!;
        Require(box.Text == blockText && box.IsKeyboardFocusWithin && PGet<bool>("IsEditingPreview"), $"The edit box shows '{blockText}' and has focus");
        box.Text = newText;
        string? result = null;
        if (enter) PKey(pw, Key.Enter); else result = Commit();
        Pump(); Pump();
        return result;
    }
    EditTo("Title", "Title Two");
    Require(ped.Text == start.Replace("# Title", "# Title Two") && pdoc.IsDirty && Box() is null, "Editing a heading in the preview updates the source and marks the note dirty");
    Require(PGet<IEnumerable<Control>>("PreviewBlocks").Any(c => BlockText(c) == "Title Two"), "The preview re-renders the edited heading");
    ped.Undo(); Pump(); Pump();
    Require(ped.Text == start && !pdoc.IsDirty, "One undo reverts the preview edit and the note is clean again");
    ped.Redo(); Pump(); Pump();
    Require(ped.Text.StartsWith("# Title Two\n"), "Redo reapplies it");
    ped.Undo(); Pump(); Pump();

    EditTo("Hello bold world.", "Hello bolder world.");
    Require(ped.Text.Contains("Hello **bolder** world.\n"), "Typing at the end of bold text extends the bold text");
    EditTo("Hello bolder world.", "Hello bolder world *x* [y]");
    Require(ped.Text.Contains("Hello **bolder** world \\*x\\* \\[y\\]\n"), "Text inside bold is edited in place and typed Markdown characters are escaped");
    Require(PGet<IEnumerable<Control>>("PreviewBlocks").Any(c => BlockText(c) == "Hello bolder world *x* [y]"), "Escaped characters render literally");
    var before = ped.Text;
    var rejected = EditTo("Hello bolder world *x* [y]", "HelloXlder world *x* [y]", enter: false);
    Require(rejected == Mdhub.Rendering.PreviewEdit.SpansFormatting && ped.Text == before, "An edit across the bold markup is rejected and the source is unchanged");
    var status = pw.GetVisualDescendants().OfType<TextBlock>().Any(b => b.Text == Mdhub.Rendering.PreviewEdit.SpansFormatting);
    Require(status, "The rejection reason shows in the status bar");
    rejected = EditTo("Hello bolder world *x* [y]", "Hello bolder world\nnext", enter: false);
    Require(rejected == Mdhub.Rendering.PreviewEdit.NoLineBreaks && ped.Text == before, "Line breaks are rejected");
    rejected = EditTo("Hello bolder world *x* [y]", "Hello bolder  world *x* [y]", enter: false);
    Require(rejected is null, "Extra spaces are plain text");
    ped.Undo(); Pump(); Pump();

    EditTo("item one", "item 1 <b>");
    Require(ped.Text.Contains("- item 1 \\<b\\>\n- item two"), "List item text is edited and HTML is escaped");
    EditTo("item two", "- nested?");
    Require(ped.Text.Contains("- \\- nested?\n"), "A list marker typed at the start of an item is escaped");
    EditTo("quoted text", "1. quoted text");
    Require(ped.Text.Contains("> 1\\. quoted text\n"), "A numbered-list starter in a quote is escaped");
    EditTo("cell", "cell | pipe");
    Require(ped.Text.Contains("| cell \\| pipe | two |") && PGet<IEnumerable<Control>>("PreviewBlocks").Any(c => BlockText(c) == "cell | pipe"), "Table cell edits escape the pipe and keep two columns");
    EditTo("Use a*b here.", "Use a*b+c here.");
    Require(ped.Text.Contains("Use `a*b+c` here."), "Inline code is edited without escaping");
    rejected = EditTo("Use a*b+c here.", "Use a*b`c here.", enter: false);
    Require(rejected == Mdhub.Rendering.PreviewEdit.Backtick, "A backtick inside inline code is rejected");

    // Code block: plain text edit, Ctrl+Enter applies, Enter adds a line.
    var codeBlock = Block("let x = 1;");
    Require(Begin(codeBlock) && Box()!.AcceptsReturn, "Fenced code blocks can be edited as plain text");
    Box()!.Text = "let x = 2;\nlet y = 3;"; Pump();
    PKey(pw, Key.Enter);
    Require(Box() is { Text: var codeTyped } && codeTyped!.Split('\n').Length == 3, "Enter adds a line instead of closing a code block edit");
    Box()!.Text = "let x = 2;\nlet y = 3;"; Pump();
    PKey(pw, Key.Enter, RawInputModifiers.Control); Pump(); Pump();
    Require(ped.Text.Contains("```js\nlet x = 2;\nlet y = 3;\n```\n") && Box() is null, "Ctrl+Enter writes the code back inside the fence");
    Require(Begin(Block("let x = 2;\nlet y = 3;")), "Edited code block can be edited again");
    before = ped.Text;
    Box()!.Text = "let x = 2;\n```\nescape";
    rejected = Commit(); Pump(); Pump();
    Require(rejected == Mdhub.Rendering.PreviewEdit.ChangesStructure && ped.Text == before, "Typing a closing fence in a code block is rejected");

    // Esc cancels; focus returns to the preview, where Ctrl+Z undoes in the source.
    Require(Begin(Block("Title")), "Begin heading edit again");
    Box()!.Text = "Changed";
    PKey(pw, Key.Escape); Pump();
    Require(Box() is null && ped.Text == before, "Esc cancels a preview edit");
    EditTo("Title", "Title!");
    Require(ped.Text.StartsWith("# Title\\!\n"), "Exclamation marks are escaped");
    Require(pmd.IsKeyboardFocusWithin && !ped.IsKeyboardFocusWithin, "Focus stays in the preview after applying");
    pfired.Clear();
    PKey(pw, Key.Z, RawInputModifiers.Control); Pump(); Pump();
    Require(ped.Text == before && pfired.SequenceEqual(["edit.undo"]), "Ctrl+Z in the preview undoes the preview edit once");
    PKey(pw, Key.Y, RawInputModifiers.Control); Pump(); Pump();
    Require(ped.Text.StartsWith("# Title\\!\n"), "Ctrl+Y in the preview redoes it");

    // Losing focus applies; a source change while editing cancels the overlay.
    Require(Begin(Block("Title!")), "Begin edit to test focus loss");
    Box()!.Text = "Title!?";
    ped.Focus(); Pump(); Pump();
    Require(Box() is null && ped.Text.StartsWith("# Title\\!?\n"), "Moving focus away applies the preview edit");
    Require(Begin(Block("Title!?")), "Begin edit to test a source change");
    Box()!.Text = "ignored";
    var edited = "# Fresh\n";
    ped.Text = edited; Pump(); Pump();
    Require(Box() is null && ped.Text == edited, "A source change while editing cancels the preview edit");
    Require(PGet<IEnumerable<Control>>("PreviewBlocks").Any(c => BlockText(c) == "Fresh"), "The preview shows the new source");

    // Undo in the source pane also undoes preview edits; plain text has nothing to edit.
    var plainDoc = (NoteDocument)typeof(MainView).GetMethod("AddDocument", pflags)!.Invoke(pview, [null, "plain text", "plain.txt", null])!; Pump(); Pump();
    Require(!PGet<IEnumerable<Control>>("PreviewBlocks").Any() && Box() is null, "Plain text notes have no editable preview blocks");
    foreach (var d in pw.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs").Items.OfType<NoteDocument>()) { d.Editor.Text = ""; d.SavedText = ""; }
    pw.Close(); Pump();
}
{
    // Item 20: "Ln 12, Col 5 (9 selected)" in the status bar.
    var cw = new MainWindow { Width = 1300, Height = 800 };
    cw.Show(); Pump();
    var cflags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var cview = cw.GetVisualDescendants().OfType<MainView>().First();
    var cmd = cw.GetVisualDescendants().OfType<MdEditor>().First();
    TextBlock Caret() => cw.GetVisualDescendants().OfType<TextBlock>().First(c => c.Name == "CaretLabel");
    string? Shown() => Caret().IsVisible ? Caret().Text : null;
    NoteDocument CAdd(string text, string name) { var d = (NoteDocument)typeof(MainView).GetMethod("AddDocument", cflags)!.Invoke(cview, [null, text, name, null])!; Pump(); return d; }
    var cdoc = CAdd("first line\r\nsecond\nthird", "caret.md");
    cdoc.Editor.Focus(); cdoc.Editor.SelectionStart = cdoc.Editor.SelectionEnd = 0; Pump();
    Require(Shown() == "Ln 1, Col 1", $"Caret position starts at Ln 1, Col 1 ({Shown()})");
    cdoc.Editor.SelectionStart = cdoc.Editor.SelectionEnd = 17; Pump();
    Require(Shown() == "Ln 2, Col 6", $"Caret moves update line and column after a CRLF ({Shown()})");
    cdoc.Editor.SelectionStart = cdoc.Editor.SelectionEnd = cdoc.Text.Length; Pump();
    Require(Shown() == "Ln 3, Col 6", $"Caret at end of text is on the last line ({Shown()})");
    cdoc.Editor.SelectionStart = 6; cdoc.Editor.SelectionEnd = 15; Pump();
    Require(Shown() == "Ln 2, Col 4 (8 selected)", $"Selection across CRLF counts the break as 1 ({Shown()})");
    cdoc.Editor.SelectionStart = 15; cdoc.Editor.SelectionEnd = 6; Pump();
    Require(Shown() == "Ln 1, Col 7 (8 selected)", $"Backward selection reports the active end ({Shown()})");
    cdoc.Editor.SelectAll(); Pump();
    Require(Shown()!.EndsWith("(23 selected)"), $"Select all counts characters with each line break as 1 ({Shown()})");
    cdoc.Editor.SelectionStart = cdoc.Editor.SelectionEnd = 3; Pump();
    Require(!Shown()!.Contains("selected"), "No selection count without a selection");
    cw.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.None, null); cw.KeyRelease(Key.Right, RawInputModifiers.Shift, PhysicalKey.None, null); Pump();
    Require(Shown() == "Ln 1, Col 5 (1 selected)", $"Keyboard selection updates live ({Shown()})");
    var tdoc = CAdd("a\nbc", "caret.txt");
    tdoc.Editor.Focus(); tdoc.Editor.SelectionStart = tdoc.Editor.SelectionEnd = 4; Pump();
    Require(Shown() == "Ln 2, Col 3", $"Plain text notes show the caret position ({Shown()})");
    cw.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs").SelectedItem = cdoc; Pump(); Pump();
    cdoc.Editor.SelectionStart = cdoc.Editor.SelectionEnd = 3; Pump();
    Require(Shown() == "Ln 1, Col 4", $"Switching tabs shows that tab's caret ({Shown()})");
    var cscroll = cmd.GetVisualDescendants().OfType<ScrollViewer>().First(c => c.Name == "PreviewScroll"); cscroll.Focusable = true; cscroll.Focus(); Pump();
    Require(Shown() is null, "Caret position hides while the preview has focus");
    cdoc.Editor.Focus(); Pump();
    Require(Shown() == "Ln 1, Col 4", "Caret position returns when the editor regains focus");
    var png = Path.Combine(Path.GetTempPath(), $"mdhub-caret-{Guid.NewGuid():N}.png");
    File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg=="));
    var openImage = (Task)typeof(MainView).GetMethod("OpenPathAsync", cflags)!.Invoke(cview, [png])!;
    for (var i = 0; i < 20 && !openImage.IsCompleted; i++) Pump();
    Pump();
    Require(Shown() is null, "Caret position hides for images (no text editor)");
    foreach (var d in cw.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs").Items.OfType<NoteDocument>().Where(d => !d.IsImage)) { d.Editor.Text = ""; d.SavedText = ""; }
    cw.Close(); Pump();
    try { File.Delete(png); } catch (IOException) { }
}{
    // Ctrl+Shift+F / Ctrl+Shift+E work from every focus state (window-level tunnelled KeyDown).
    var fw = new MainWindow { Width = 1300, Height = 800 };
    fw.Show(); Pump();
    var fflags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var fview = fw.GetVisualDescendants().OfType<MainView>().First();
    T FFind<T>(string name) where T : Control => fw.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    var froot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"mdhub-focus-{Guid.NewGuid():N}")).FullName;
    File.WriteAllText(Path.Combine(froot, "a.md"), "# A\n\ntext");
    var fload = (Task)typeof(MainView).GetMethod("LoadRootAsync", fflags)!.Invoke(fview, [froot])!;
    for (var i = 0; i < 20 && !fload.IsCompleted; i++) Pump();
    var fdoc = (NoteDocument)typeof(MainView).GetMethod("AddDocument", fflags)!.Invoke(fview, [null, "# Title\n\nbody", "focus.md", null])!; Pump(); Pump();
    void Chord(Key key) { fw.KeyPress(key, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null); fw.KeyRelease(key, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null); Pump(); }
    bool SearchFocused() => FFind<GlobalSearch>("GlobalSearch").IsVisible && FFind<TextBox>("GlobalQuery").IsFocused;
    bool ExplorerFocused() => FFind<FolderExplorer>("FolderExplorer").IsVisible && !FFind<GlobalSearch>("GlobalSearch").IsVisible && FFind<FolderExplorer>("FolderExplorer").IsKeyboardFocusWithin;
    var focusStates = new (string Name, Action Focus)[]
    {
        ("the editor", () => fdoc.Editor.Focus()),
        ("the explorer", () => FFind<FolderExplorer>("FolderExplorer").FocusTree()),
        ("the preview", () => { var s = FFind<ScrollViewer>("PreviewScroll"); s.Focusable = true; s.Focus(); }),
        ("an empty window", () => { Chord(Key.F); FFind<TextBox>("GlobalQuery").Focus(); Pump(); FFind<GlobalSearch>("GlobalSearch").IsVisible = false; Pump(); }),
        ("after Ctrl+Shift+E", () => Chord(Key.E)),
        ("the search box", () => { Chord(Key.F); FFind<TextBox>("GlobalQuery").Text = "abc"; }),
        ("a search option toggle", () => { Chord(Key.F); FFind<Avalonia.Controls.Primitives.ToggleButton>("GlobalRegex").Focus(); }),
        ("the search results", () => { Chord(Key.F); FFind<TreeView>("GlobalResults").Focus(); }),
        ("a hidden sidebar", () => { typeof(MainView).GetMethod("ToggleFolders_Click", fflags)!.Invoke(fview, [null, new RoutedEventArgs()]); Pump(); fdoc.Editor.Focus(); }),
    };
    foreach (var (name, focus) in focusStates)
    {
        focus(); Pump();
        if (name == "an empty window") Require(TopLevel.GetTopLevel(fw)!.FocusManager!.GetFocusedElement() is null, $"Nothing inside the window has focus in the empty-window case ({TopLevel.GetTopLevel(fw)!.FocusManager!.GetFocusedElement()})");
        Chord(Key.F);
        Require(SearchFocused(), $"Ctrl+Shift+F opens global search and focuses its box from {name}");
        if (name == "the search box") Require(FFind<TextBox>("GlobalQuery").SelectedText == "abc", "Ctrl+Shift+F in the search box selects its text");
    }
    foreach (var (name, focus) in focusStates)
    {
        if (name == "after Ctrl+Shift+E") continue;
        focus(); Pump();
        if (name == "a hidden sidebar") Require(!FFind<Grid>("Sidebar").IsVisible || !FFind<FolderExplorer>("FolderExplorer").IsEffectivelyVisible, "Sidebar is hidden before Ctrl+Shift+E");
        Chord(Key.E);
        Require(ExplorerFocused(), $"Ctrl+Shift+E shows the explorer and focuses it from {name} (visible={FFind<FolderExplorer>("FolderExplorer").IsVisible}, focus={(TopLevel.GetTopLevel(fw)!.FocusManager!.GetFocusedElement() as Control)?.GetType().Name}:{(TopLevel.GetTopLevel(fw)!.FocusManager!.GetFocusedElement() as Control)?.Name})");
    }
    Chord(Key.H);
    Require(SearchFocused() && FFind<StackPanel>("GlobalReplaceRow").IsVisible, "Ctrl+Shift+H opens search with replace from the explorer");
    fdoc.Editor.Text = ""; fdoc.SavedText = "";
    foreach (var d in fw.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs").Items.OfType<NoteDocument>()) d.SavedText = d.Text;
    fw.Close(); Pump();
    try { Directory.Delete(froot, true); } catch (IOException) { }
}{
    // Item 26: I-beam over the editor text area in md and txt tabs; igutter and scrollbars keep the arrow.
    var iw = new MainWindow { Width = 1300, Height = 800 };
    iw.Show(); Pump();
    var iflags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var iview = iw.GetVisualDescendants().OfType<MainView>().First();
    static string? CursorKind(Cursor? cursor)
    {
        if (cursor is null) return null;
        object? Probe(object o, int depth)
        {
            if (depth > 3) return null;
            foreach (var f in o.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
            {
                var v = f.GetValue(o);
                if (v is StandardCursorType s) return s;
                if (v is not null && !f.FieldType.IsPrimitive && f.FieldType != typeof(string) && Probe(v, depth + 1) is { } inner) return inner;
            }
            return null;
        }
        return Probe(cursor, 0)?.ToString() ?? cursor.ToString();
    }
    // The cursor an element shows: its own Cursor, inherited from ancestors (null means the default arrow).
    string Effective(IInputElement? element) => CursorKind((element as InputElement)?.Cursor) ?? "Arrow";
    IInputElement? HitAt(Control target, Point local) => iw.InputHitTest(target.TranslatePoint(local, iw)!.Value);
    var longText = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"line {i} find me"));
    foreach (var (iname, itext) in new[] { ("cursor.md", "# Heading\n\n" + longText), ("cursor.txt", longText) })
    {
        var idoc = (NoteDocument)typeof(MainView).GetMethod("AddDocument", iflags)!.Invoke(iview, [null, itext, iname, null])!; Pump(); Pump();
        iw.UpdateLayout();
        var ied = idoc.Editor;
        var presenter = ied.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().First();
        Require(Effective(HitAt(presenter, new Point(20, 8))) == "Ibeam", $"I-beam over the text in {iname} (got {Effective(HitAt(presenter, new Point(20, 8)))})");
        Require(Effective(HitAt(ied, new Point(ied.Bounds.Width / 2, ied.Bounds.Height - 30))) == "Ibeam", $"I-beam over the lower text area in {iname}");
        var scrollBar = ied.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>().First(b => b.Orientation == Avalonia.Layout.Orientation.Vertical && b.IsEffectivelyVisible && b.Bounds.Height > 0);
        Require(Effective(HitAt(scrollBar, new Point(scrollBar.Bounds.Width / 2, scrollBar.Bounds.Height / 2))) == "Arrow", $"Vertical scrollbar keeps the arrow in {iname} (got {Effective(HitAt(scrollBar, new Point(scrollBar.Bounds.Width / 2, scrollBar.Bounds.Height / 2)))})");
        var igutter = iw.GetVisualDescendants().OfType<LineNumberGutter>().First(g => g.IsEffectivelyVisible);
        Require(Effective(HitAt(igutter, new Point(igutter.Bounds.Width / 2, 10))) == "Arrow", $"Line-number gutter keeps the arrow in {iname}");
        // Overlays drawn over the text must let the pointer through to the editor.
        var findBar = iw.GetVisualDescendants().OfType<FindBar>().First();
        ied.Focus();
        iw.GetVisualDescendants().OfType<TextEditor>().First().OpenFind(false); Pump();
        var findInput = findBar.GetVisualDescendants().OfType<TextBox>().First(b => b.Classes.Contains("findField")); findInput.Text = "find"; Pump();
        var overlays = iw.GetVisualDescendants().Where(v => v is CurrentLineOverlay or FindHighlightOverlay).Cast<Control>().ToList();
        Require(overlays.Count >= 2 && overlays.All(o => !o.IsHitTestVisible), $"Current-line and find-highlight overlays are hit-test invisible in {iname}");
        var underOverlay = HitAt(ied, new Point(ied.Bounds.Width / 2, ied.Bounds.Height / 2));
        Require(underOverlay is Visual hv && (hv == ied || hv.GetVisualAncestors().Contains(ied)) && Effective(underOverlay) == "Ibeam", $"Pointer over highlighted text reaches the editor with the I-beam in {iname}");
        iw.GetVisualDescendants().OfType<TextEditor>().First().CloseFind(); Pump();
        idoc.SavedText = idoc.Text;
    }
    foreach (var d in iw.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs").Items.OfType<NoteDocument>()) d.SavedText = d.Text;
    iw.Close(); Pump();
}if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("MDHUB_TEST_RECYCLE") == "1")
{
    var recycleRoot = Path.Combine(Path.GetTempPath(), $"mdhub-recycle-{Guid.NewGuid():N}");
    Directory.CreateDirectory(Path.Combine(recycleRoot, "dir"));
    File.WriteAllText(Path.Combine(recycleRoot, "dir", "x.md"), "x");
    File.WriteAllText(Path.Combine(recycleRoot, "y.md"), "y");
    Mdhub.Services.ExplorerFileOperations.Recycle(recycleRoot, Path.Combine(recycleRoot, "y.md"));
    Mdhub.Services.ExplorerFileOperations.Recycle(recycleRoot, Path.Combine(recycleRoot, "dir"));
    Require(!File.Exists(Path.Combine(recycleRoot, "y.md")) && !Directory.Exists(Path.Combine(recycleRoot, "dir")) && !Directory.Exists(Path.Combine(recycleRoot, ".md-trash")),
        "Recycle sends files and folders to the Recycle Bin");
    Directory.Delete(recycleRoot, true);
}
Console.WriteLine("All UI tests passed.");

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}

void RunOperation(string method, params object[] arguments)
{
    var task = (Task)typeof(MainView).GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window.FindControl<MainView>("MainView"), arguments)!;
    while (!task.IsCompleted) Pump();
    task.GetAwaiter().GetResult();
}

static string InlineString(TextBlock block) => block.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.Select(InlinePart)) : block.Text ?? "";
static string InlinePart(Avalonia.Controls.Documents.Inline inline) => inline switch
{
    Avalonia.Controls.Documents.Run run => run.Text ?? "",
    Avalonia.Controls.Documents.Span span => string.Concat(span.Inlines.Select(InlinePart)),
    _ => ""
};

static void Pump()
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
    Dispatcher.UIThread.MainLoop(timeout.Token);
}


T? Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().SingleOrDefault(control => control.Name == name);

void ClickMenu(string header)
{
    var menu = Descendants(Find<Menu>("MainMenu")!.Items.OfType<MenuItem>()).Single(item => Equals(item.Header, header));
    menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Dispatcher.UIThread.RunJobs();
}

static IEnumerable<MenuItem> Descendants(IEnumerable<MenuItem> items)
{
    foreach (var item in items)
    {
        yield return item;
        foreach (var child in Descendants(item.Items.OfType<MenuItem>())) yield return child;
    }
}

void ClickDialog(string choice)
{
    Dispatcher.UIThread.RunJobs();
    var dialog = window.OwnedWindows.Single();
    var button = dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, choice));
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump();
}

void EnterName(string name)
{
    Dispatcher.UIThread.RunJobs();
    var dialog = window.OwnedWindows.Single();
    dialog.GetVisualDescendants().OfType<TextBox>().Single().Text = name;
    dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Create")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    var deadline = DateTime.UtcNow.AddSeconds(10);
    do { Pump(); } while (!Find<Button>("NewFileButton")!.IsEffectivelyEnabled && DateTime.UtcNow < deadline);
    Require(window.OwnedWindows.Count == 0, "Creation dialog completes successfully");
}

void Await(Task task)
{
    var deadline = DateTime.UtcNow.AddSeconds(10);
    while (!task.IsCompleted && DateTime.UtcNow < deadline) Pump();
    Require(task.IsCompleted, "Explorer operation completes");
    task.GetAwaiter().GetResult();
}

static IEnumerable<TreeViewItem> TreeNodes(IEnumerable<TreeViewItem> items)
{
    foreach (var item in items)
    {
        yield return item;
        foreach (var child in TreeNodes(item.Items.OfType<TreeViewItem>())) yield return child;
    }
}

TreeViewItem? TreeItem(string path) => TreeNodes(Find<TreeView>("FolderTree")!.Items.OfType<TreeViewItem>())
    .FirstOrDefault(i => i.Tag is Mdhub.Services.FolderEntry e && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));

string? SelectedPath() => (Find<TreeView>("FolderTree")!.SelectedItem as TreeViewItem)?.Tag is Mdhub.Services.FolderEntry e ? e.Path : null;

void ClickContext(string path, string menuName)
{
    var item = TreeItem(path) ?? throw new Exception("Tree item not found: " + path);
    item.ContextMenu!.Items.OfType<MenuItem>().Single(m => m.Name == menuName).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Pump();
}

void WaitFor(Func<bool> condition)
{
    var deadline = DateTime.UtcNow.AddSeconds(10);
    while (!condition() && DateTime.UtcNow < deadline) Pump();
    Require(condition(), "Explorer action completes");
}

void RemoveTab(NoteDocument document) { tabs.SelectedItem = document; ClickMenu("_Close Tab"); Pump(); }

void ClickMenuIn(Window w, string header)
{
    var menu = Descendants(w.GetVisualDescendants().OfType<Menu>().Single(m => m.Name == "MainMenu").Items.OfType<MenuItem>()).Single(item => Equals(item.Header, header));
    menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Dispatcher.UIThread.RunJobs();
}
