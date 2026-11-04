using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Markdig.Syntax;
using Mdhub;
using Mdhub.Rendering;

internal static class NestedListTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new Window { Width = 600, Height = 600 };
        window.Show();
        try
        {
            foreach (var indent in new[] { "  ", "   ", "    ", "\t" })
            {
                var source = $"1. itme\n{indent}- asdasd\n{indent}- asddd\n2. asd\n3. oonsdfdd\n4. dfhisdf";
                var document = MarkdownPreview.Parse(source);
                var ordered = document.OfType<ListBlock>().Single();
                Check(ordered.IsOrdered && ordered.Count == 4 && ((ListItemBlock)ordered[0]).OfType<ListBlock>().Single().Count == 2,
                    "Indented bullets belong to the first numbered item");
                var rendered = MarkdownPreview.RenderMapped(document, source, null);
                var panel = new StackPanel();
                foreach (var control in rendered.Controls) panel.Children.Add(control);
                window.Content = panel;
                window.UpdateLayout();
                TextBlock Text(string value) => panel.GetVisualDescendants().OfType<TextBlock>()
                    .Single(t => t.Inlines?.OfType<Run>().Any(r => r.Text == value) == true);
                double X(TextBlock text) => text.TranslatePoint(default, panel)!.Value.X;
                Check(X(Text("asdasd")) > X(Text("itme")) && X(Text("asddd")) == X(Text("asdasd")),
                    "Both bullet items render one level to the right");
                Check(Math.Abs(X(Text("asd")) - X(Text("itme"))) < 1,
                    "Following numbered items retain their original indentation");
                var bulletStart = source.IndexOf("asddd", StringComparison.Ordinal);
                Check(rendered.Map.Map(bulletStart, bulletStart + 5).Any(h => !h.WholeBlock && h.Length == 5),
                    "Nested bullet selection retains original source offsets");
            }
            Check(MarkdownPreview.Parse("1. first\n- separate\n2. second").OfType<ListBlock>().Count() == 3,
                "Unindented bullets stay at the top level");
            Check(MarkdownPreview.Parse("1. first\n\nParagraph\n  - separate").Count == 3,
                "An intervening paragraph prevents nesting");
            Check(MarkdownPreview.Parse("```\n1. first\n  - code\n```").Single() is FencedCodeBlock,
                "List-like code remains code");
        }
        finally { window.Close(); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}
