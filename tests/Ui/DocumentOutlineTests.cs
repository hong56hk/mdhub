using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Views;

internal static class DocumentOutlineTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump(int ms = 60) { using var timeout = new CancellationTokenSource(ms); Dispatcher.UIThread.MainLoop(timeout.Token); }
        var window = new MainWindow { Width = 1200, Height = 760 };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            window.Show(); Pump();
            var view = window.GetVisualDescendants().OfType<MainView>().Single();
            var bar = window.GetVisualDescendants().OfType<TextFormatter>().Single();
            var outline = view.FindControl<DocumentOutline>("Outline")!;
            var tree = outline.FindControl<TreeView>("Headings")!;
            var toggle = bar.FindControl<Button>("OutlineButton")!;
            NoteDocument Active() => (NoteDocument)typeof(MainView).GetField("_active", flags)!.GetValue(view)!;
            void Click(string name) { bar.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(300); }
            TreeViewItem Root() => (TreeViewItem)tree.Items[0]!;
            string Title(TreeViewItem item) => ((TextBlock)item.Header!).Text!;
            Check(!outline.IsVisible && toggle.IsEnabled, "Outline is hidden by default for Markdown");
            var document = Active();
            document.Editor.Text = "# Main **title**\n\n## Child `code`\n\n#### Skipped level\n\n```md\n# Not a heading\n```\n\nSecond root\n===========\n\n## [Link](https://example.com)\n";
            Click("OutlineButton");
            Check(outline.IsVisible && toggle.Classes.Contains("on"), "Toolbar toggle opens and highlights the outline");
            Check(tree.Items.Count == 2 && Title(Root()) == "Main title", "ATX and Setext roots are parsed and formatting removed");
            var child = (TreeViewItem)Root().Items[0]!;
            Check(Title(child) == "Child code" && child.Items.Count == 1, "Nested and skipped heading levels form a tree");
            Check(((TreeViewItem)tree.Items[1]!).Items.Count == 1, "Code fences do not create headings");
            using (var frame = window.CaptureRenderedFrame())
                frame?.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mdhub-outline-preview.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            var preview = view.GetVisualDescendants().OfType<MdEditor>().Single();
            foreach (var mode in new[] { "EditorOnlyButton", "TogglePreviewButton", "ViewerOnlyButton" })
            {
                Click(mode);
                var centers = (int)typeof(MdEditor).GetProperty("PreviewCenters", flags)!.GetValue(preview)!;
                tree.SelectedItem = null;
                tree.SelectedItem = child; Pump();
                if (mode == "ViewerOnlyButton")
                    Check((int)typeof(MdEditor).GetProperty("PreviewCenters", flags)!.GetValue(preview)! > centers && preview.IsKeyboardFocusWithin, "Viewer-only navigation scrolls and focuses the preview");
                else Check(document.Editor.CaretIndex == document.Text.IndexOf("## Child", StringComparison.Ordinal), mode + ": heading navigation reaches source position");
                Check(outline.IsVisible, mode + ": outline stays visible");
            }
            Click("EditorOnlyButton");
            Root().IsExpanded = false;
            document.Editor.Text += "\n# Added\n"; Pump(400);
            Check(tree.Items.Count == 3 && !Root().IsExpanded, "Live edits update headings and retain collapsed branches");
            Click("OutlineButton");
            Check(!outline.IsVisible && !toggle.Classes.Contains("on"), "Toolbar toggle hides the outline");
            document.Editor.Text = "No headings here"; Pump(300); Click("OutlineButton");
            Check(tree.Items.Count == 0 && outline.FindControl<TextBlock>("EmptyMessage")!.IsVisible, "Empty outline has an explanatory message");
            typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, new object?[] { null, "# Other", "Other.md", null }); Pump();
            Check(tree.Items.Count == 1 && Title(Root()) == "Other", "Switching tabs refreshes the outline");
            typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, new object?[] { null, "# Plain", "Other.txt", null }); Pump();
            Check(!outline.IsVisible && !toggle.IsEnabled, "Non-Markdown tabs hide and disable the outline");
            typeof(MainView).GetMethod("SelectDocument", flags)!.Invoke(view, new object[] { document }); Pump();
            Check(outline.IsVisible, "Returning to Markdown restores the requested outline visibility");
            outline.GetVisualDescendants().OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(!outline.IsVisible, "Panel close button hides the outline");
        }
        finally { window.Close(); Pump(); }
        Console.WriteLine($"Document outline tests: {passed} passed");
    }
}
