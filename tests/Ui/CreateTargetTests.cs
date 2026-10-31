using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 42: New file / New folder follow the explorer selection, show the target, then reveal the new item.
internal static class CreateTargetTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        bool Until(Func<bool> ok) { var end = DateTime.UtcNow.AddSeconds(8); while (!ok() && DateTime.UtcNow < end) Pump(); return ok(); }
        var root = Path.Combine(Path.GetTempPath(), $"mdhub-create-{Guid.NewGuid():N}");
        var rootName = Path.GetFileName(root);
        var docs = Path.Combine(root, "docs"); var deep = Path.Combine(docs, "deep");
        Directory.CreateDirectory(deep);
        var readme = Path.Combine(deep, "readme.md"); File.WriteAllText(readme, "x");
        File.WriteAllText(Path.Combine(root, "top.md"), "x");
        var w = new MainWindow { Width = 1100, Height = 700 };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var explorer = w.GetVisualDescendants().OfType<FolderExplorer>().Single();
            var tree = explorer.FindControl<TreeView>("FolderTree")!;
            Wait(v.OpenFolderPathAsync(root));
            TreeViewItem? Row(string p) => tree.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault(t => t.Tag is FolderEntry e && string.Equals(e.Path, p, StringComparison.OrdinalIgnoreCase));
            bool SelectedIs(string p) => tree.SelectedItem is TreeViewItem { Tag: FolderEntry e } && string.Equals(e.Path, p, StringComparison.OrdinalIgnoreCase);
            void Select(string p) { Wait(explorer.RevealAsync(p, quiet: true)); Check(SelectedIs(p), $"(setup) {Path.GetFileName(p)} is selected"); }
            // Clicks New file / New folder, checks the dialog's "Create in:" line, types a name and creates.
            string Create(bool folder, string expectedDir, string expectedText, string name)
            {
                Check(explorer.GetCreationDirectory() == expectedDir, $"{(folder ? "New folder" : "New file")} targets {expectedText}");
                explorer.FindControl<Button>(folder ? "NewFolderButton" : "NewFileButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Until(() => w.OwnedWindows.Count == 1), "(setup) the name dialog opens");
                var dlg = w.OwnedWindows.Single();
                var line = dlg.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CreateInText");
                Check(line.Text == Loc.F("CreateIn", expectedText) && (ToolTip.GetTip(line) as string) == expectedDir, $"The dialog says \"{line.Text}\" (full path in its tooltip)");
                dlg.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ItemName").Text = name;
                dlg.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Loc.T("Create"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var created = Path.Combine(expectedDir, name);
                Check(Until(() => w.OwnedWindows.Count == 0 && (folder ? Directory.Exists(created) : File.Exists(created))), $"{name} is created in {expectedText}");
                Check(Until(() => SelectedIs(created) && Row(created) is { } r && r.IsVisible), $"{name} is revealed and selected in the explorer");
                return created;
            }

            tree.SelectedItem = null; Pump();
            Create(false, root, rootName, "rootnote.md");
            Select(deep);
            var inDeep = Create(false, deep, Path.Combine(rootName, "docs", "deep"), "inside.md");
            Check(Row(deep)?.IsExpanded == true, "The selected folder is expanded to show the new file");
            Select(readme);
            Create(true, deep, Path.Combine(rootName, "docs", "deep"), "beside");
            Select(Path.Combine(root, "top.md"));
            var topFolder = Create(true, root, rootName, "topfolder");
            Select(topFolder);
            Create(false, topFolder, Path.Combine(rootName, "topfolder"), "a.md");
            Check(explorer.DisplayDirectory(Path.GetTempPath()) == Path.GetTempPath(), "A directory outside the open folder is shown as its full path");
            Check(File.Exists(inDeep) && !File.Exists(Path.Combine(docs, "inside.md")), "Nothing is created in the wrong folder");
        }
        finally
        {
            foreach (var o in w.OwnedWindows.ToList()) o.Close();
            try { w.Close(); Pump(); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine($"Create target tests: {passed} passed");
    }
}