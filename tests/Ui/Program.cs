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

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var window = new MainWindow();
window.Show();
Dispatcher.UIThread.RunJobs();
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
Await(explorer.RevealCreatedAsync(seed));
Pump();
tabs.SelectedItem = opened; // Explorer selection must win over the active root-level file.
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
Require(Directory.Exists(Path.Combine(creationFixture, "Another folder")), "A selected folder creates a sibling at the same level");

tree.SelectedItem = null;
Find<Button>("NewFileButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
EnterName("fallback.txt");
Require(File.Exists(Path.Combine(creationFixture, "fallback.txt")), "No explorer selection falls back to the active file's directory");
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
Require(explorer.GetCreationDirectory(second.Path) == fixture, "An untitled active document falls back to the opened folder");
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
Require(!Find<FolderExplorer>("FolderExplorer")!.IsVisible, "View menu hides the folder component");
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
Require(preview.Children.OfType<SelectableTextBlock>().Any(t => t.Text == "A plain-text note"), "Plain text preview remains literal");
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

Find<Button>("CloseWindowButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Require(window.IsVisible && window.OwnedWindows.Count == 1, "Custom close button preserves the unsaved-change confirmation");
ClickDialog("Cancel");
Require(window.IsVisible && second.IsDirty, "Cancel keeps the application and unsaved text open");
second.SavedText = second.Text;
window.Close();
Require(!window.IsVisible, "Clean window closes normally");
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
