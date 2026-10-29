using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Text;

internal static class SyntaxTests
{
    public static void Run()
    {
        const string text = "# Heading\n\n1. First\n2. Second\n   - Nested\n\n- Bullet\n\n```md\n# Code\n- Code\n```\n\n---\nordinary # text\n";
        var markers = MarkdownSyntax.Markers(text);
        Check(markers.Select(m => text.Substring(m.Start, m.Length)).SequenceEqual(new[] { "#", "1.", "2.", "-", "-" }),
            "only Markdown heading/list markers are colored, excluding fences and horizontal rules");
        Check(markers.Select(m => m.Kind).Distinct().Count() == 3, "three distinct syntax categories");
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow(); window.Show();
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        Layout();
        var tabs = window.GetVisualDescendants().OfType<ListBox>().Single(x => x.Name == "Tabs");
        var document = (NoteDocument)tabs.SelectedItem!;
        var editor = (MarkdownTextBox)document.Editor;
        editor.Text = text;
        document.Refresh(); Layout();
        var presenter = editor.GetVisualDescendants().OfType<MarkdownTextPresenter>().Single();
        var brushes = presenter.TextLayout.TextLines.SelectMany(l => l.TextRuns).Select(r => r.Properties?.ForegroundBrush).ToList();
        Check(brushes.Contains(MarkdownTextPresenter.HeadingBrush) && brushes.Contains(MarkdownTextPresenter.OrderedListBrush)
            && brushes.Contains(MarkdownTextPresenter.UnorderedListBrush), "all three colors reach rendered text runs");
        var originalHeight = presenter.TextLayout.Height;
        editor.HighlightMarkdown = false; Layout();
        Check(Math.Abs(presenter.TextLayout.Height - originalHeight) < 0.01, "coloring preserves text layout and line positions");
        editor.HighlightMarkdown = true;
        editor.Focus(); editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 0;
        editor.SelectedText = "#"; Layout();
        Check(editor.Text!.StartsWith("## Heading") && MarkdownSyntax.Markers(editor.Text)[0].Length == 2, "typing refreshes syntax markers");
        editor.Undo(); Layout();
        Check(editor.Text == text, "syntax coloring preserves undo");
        editor.TextWrapping = TextWrapping.Wrap;
        editor.FontSize = 24;
        editor.Text = "# " + new string('x', 180) + "\n12. item";
        Layout();
        Check(presenter.TextLayout.TextLines.Count > 2, "highlighted text supports word wrap and zoom");
        document.Path = "plain.txt"; document.Refresh(); Layout();
        Check(!presenter.HighlightMarkdown && !presenter.TextLayout.TextLines.SelectMany(l => l.TextRuns)
            .Any(r => r.Properties?.ForegroundBrush == MarkdownTextPresenter.HeadingBrush), "plain text has no Markdown syntax colors");
        document.SavedText = document.Text;
        window.Close();
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
