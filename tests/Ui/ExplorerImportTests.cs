using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Services;
using Mdhub.Views;

internal static class ExplorerImportTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var window = new MainWindow();
        var testRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-import-" + Guid.NewGuid().ToString("N"))).FullName;
        var passed = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
        void Pump() { using var timeout = new CancellationTokenSource(30); Dispatcher.UIThread.MainLoop(timeout.Token); }
        try
        {
            window.Show(); Pump();
            var view = window.FindControl<MainView>("MainView")!;
            var explorer = window.GetVisualDescendants().OfType<FolderExplorer>().Single();
            explorer.WatchEnabled = false;
            var tree = explorer.FindControl<TreeView>("FolderTree")!;
            var rootButton = explorer.FindControl<Button>("FolderNameButton")!;
            var workspace = Directory.CreateDirectory(Path.Combine(testRoot, "workspace")).FullName;
            var nested = Directory.CreateDirectory(Path.Combine(workspace, "Nested")).FullName;
            var outside = Directory.CreateDirectory(Path.Combine(testRoot, "outside")).FullName;
            var source = Path.Combine(outside, "note.md");
            var second = Path.Combine(outside, "photo.bin");
            File.WriteAllText(source, "original note"); File.WriteAllBytes(second, [0, 1, 2, 255]);
            void Wait(Task task)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!task.IsCompleted && DateTime.UtcNow < deadline) Pump();
                Check(task.IsCompleted, "operation completes"); task.GetAwaiter().GetResult(); Pump();
            }
            void Idle()
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while ((bool)typeof(MainView).GetField("_busy", flags)!.GetValue(view)! && DateTime.UtcNow < deadline) Pump();
                Check(!(bool)typeof(MainView).GetField("_busy", flags)!.GetValue(view)!, "drop operation completes"); Pump();
            }
            DataTransfer Files(params string[] paths)
            {
                var data = new DataTransfer();
                foreach (var path in paths)
                {
                    IStorageItem? item = Directory.Exists(path) ? window.StorageProvider.TryGetFolderFromPathAsync(path).GetAwaiter().GetResult()
                        : window.StorageProvider.TryGetFileFromPathAsync(path).GetAwaiter().GetResult();
                    data.Add(DataTransferItem.CreateFile(item!));
                }
                return data;
            }
            DragEventArgs Drag(Control target, IDataTransfer data, bool drop = false, DragDropEffects effects = DragDropEffects.Copy | DragDropEffects.Move)
            {
                var e = new DragEventArgs(drop ? DragDrop.DropEvent : DragDrop.DragOverEvent, data, target, default, KeyModifiers.None) { DragEffects = effects };
                target.RaiseEvent(e); return e;
            }
            using var single = Files(source);
            Check(Drag(tree, single).DragEffects == DragDropEffects.None, "External drops require an opened folder");
            Wait((Task)typeof(MainView).GetMethod("LoadRootAsync", flags)!.Invoke(view, [workspace])!);
            Check(Drag(tree, single).DragEffects == DragDropEffects.Copy && rootButton.Classes.Contains("dropTarget"), "Empty explorer area accepts copy and highlights the root");
            Drag(tree, single, true); Idle();
            Check(File.ReadAllText(Path.Combine(workspace, "note.md")) == "original note" && File.ReadAllText(source) == "original note", "Dropping a file copies it and keeps the original");
            Check(!rootButton.Classes.Contains("dropTarget"), "Drop clears the target highlight");
            Check(tree.SelectedItem is TreeViewItem { Tag: FolderEntry selected } && selected.Path == Path.Combine(workspace, "note.md"), "Imported file is revealed and selected");
            Drag(rootButton, single, true); Idle();
            Check(File.Exists(Path.Combine(workspace, "note-copy.md")), "Folder-name drop keeps both files on a name collision");
            Drag(rootButton, single, true); Idle();
            Check(File.Exists(Path.Combine(workspace, "note-copy-2.md")), "Repeated collisions use incrementing copy names");
            var folderRow = tree.Items.OfType<TreeViewItem>().Single(i => i.Tag is FolderEntry e && e.Path == nested);
            using var multiple = Files(source, second);
            Check(Drag(folderRow, multiple).DragEffects == DragDropEffects.Copy && folderRow.Classes.Contains("dropTarget"), "A subfolder is the destination when hovered");
            Drag(folderRow, multiple, true); Idle();
            Check(File.Exists(Path.Combine(nested, "note.md")) && File.ReadAllBytes(Path.Combine(nested, "photo.bin")).SequenceEqual(new byte[] { 0, 1, 2, 255 }), "Multiple files, including binary files, copy into a subfolder");
            var fileRow = tree.Items.OfType<TreeViewItem>().Single(i => i.Tag is FolderEntry e && e.Path == Path.Combine(workspace, "note.md"));
            using var binary = Files(second);
            Drag(fileRow, binary, true); Idle();
            Check(File.Exists(Path.Combine(workspace, "photo.bin")), "Dropping on a file uses its parent folder");
            using var text = new DataTransfer(); text.Add(DataTransferItem.CreateText(source));
            Check(Drag(tree, text).DragEffects == DragDropEffects.None, "Plain text is not interpreted as a file drop");
            using var folderData = Files(outside);
            Check(Drag(tree, folderData).DragEffects == DragDropEffects.None, "Folder drops are not treated as files");
            Check(Drag(tree, single, effects: DragDropEffects.Move).DragEffects == DragDropEffects.None, "External drops never request a destructive move");
            explorer.SetBusy(true);
            Check(Drag(tree, single).DragEffects == DragDropEffects.None, "Busy explorer rejects drops");
            explorer.SetBusy(false);
            Check(ExplorerImport.Destination(workspace, source, outside) is null, "Destination must be inside the opened folder");
            Check(ExplorerImport.Destination(workspace, Path.Combine(outside, "missing.md"), workspace) is null, "Unavailable sources are rejected");
            Check(File.Exists(source) && File.Exists(second), "All external originals remain intact");
        }
        finally
        {
            window.Close();
            if (ExplorerFileOperations.Contains(Path.GetTempPath(), testRoot) && Path.GetFileName(testRoot).StartsWith("mdhub-import-")) Directory.Delete(testRoot, true);
        }
        Console.WriteLine($"{passed} external drop checks passed.");
    }
}
