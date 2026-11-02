using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Rendering;
using Mdhub.Services;
using Mdhub.Text;

internal static class CodeSyntaxTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    public static void Run()
    {
        var tokenizer = new CodeHighlighter.Tokenizer();
        SyntaxColor ColorAt(string text, string language, string needle) => tokenizer.Run(text, language).Spans.First(s => s.Start <= text.IndexOf(needle, StringComparison.Ordinal) && s.End > text.IndexOf(needle, StringComparison.Ordinal)).Color;
        foreach (var (language, sample, keyword) in new[]
        {
            ("py", "def hello(): return 42", "def"), ("c", "static int n = 42;", "static"),
            ("cpp", "constexpr int n = 42;", "constexpr"), ("cs", "public int n = 42;", "public"),
            ("fs", "let n = 42", "let"), ("vb", "Dim n As Integer = 42", "Dim"),
            ("java", "public class Hello {}", "public"), ("swift", "let n = 42", "let"),
            ("go", "package main", "package"), ("js", "const n = 42", "const"),
            ("ts", "interface Hello {}", "interface"), ("sql", "SELECT n FROM t", "SELECT"),
            ("mongodb", "const rows = db.users.find({n: 42})", "const"), ("json", "{\"ok\": true}", "true"),
            ("bash", "if true; then echo hi; fi", "if"), ("ps1", "function Hello {}", "function"),
            ("yaml", "ok: true", "true")
        })
        {
            Check(ColorAt(sample, language, keyword) == SyntaxColor.CodeKeyword, language + " keywords");
            var fenced = $"```{language}\n{sample}\n```";
            Check(MarkdownHighlighter.Highlight(fenced).Spans.Any(s => s.Color == SyntaxColor.CodeKeyword && fenced.Substring(s.Start, s.Length) == keyword), language + " Markdown fences");
        }
        Check(ColorAt("<div class=\"x\">hi</div>", "html", "div") == SyntaxColor.CodeTag, "HTML tags");
        Check(ColorAt(".a {\n color: red;\n}", "css", "color") == SyntaxColor.CodeProperty, "CSS properties");
        Check(ColorAt("<node id=\"1\"/>", "xml", "id") == SyntaxColor.CodeAttribute, "XML attributes");
        foreach (var extension in new[] { ".PYW", ".cc", ".cxx", ".hh", ".hxx", ".hpp", ".cs", ".fsx", ".vb", ".java", ".swift", ".go", ".mjs", ".cjs", ".tsx", ".jsx", ".css", ".htm", ".sql", ".json5", ".zsh", ".psm1", ".psd1", ".axaml" })
            Check(CodeHighlighter.LanguageForExtension(extension) is not null && FileSupport.IsTextByExtension("sample" + extension), extension + " detected as source text");
        Check(CodeHighlighter.LanguageForExtension(".txt") is null, "Plain text has no source language");
        foreach (var (language, text, needle) in new[]
        {
            ("cpp", "/* opening\ncomment */ int n;", "comment"),
            ("py", "s = \"\"\"opening\n# string\n\"\"\"\nx = 1", "# string"),
            ("java", "String s = \"\"\"\ntext\n\"\"\";", "text"),
            ("swift", "let s = \"\"\"\ntext\n\"\"\"", "text"),
            ("go", "s := `raw\ntext`", "text"),
            ("js", "const s = `hello\ntext`;", "text"),
            ("cs", "var s = @\"hello\ntext\";", "text")
        }) Check(ColorAt(text, language, needle) == (language == "cpp" ? SyntaxColor.CodeComment : SyntaxColor.CodeString), language + " multiline state");
        Check(ColorAt("/*\nreturn 1;", "cpp", "return") == SyntaxColor.CodeComment
            && ColorAt("//\nreturn 1;", "cpp", "return") == SyntaxColor.CodeKeyword, "Changing a delimiter updates cached later lines");

        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        SessionManager.Enabled = false;
        EditorBehavior.Import(null);
        Check(EditorBehavior.Current.CodeSyntaxHighlighting, "Highlighting defaults on");
        Check(System.Text.Json.JsonSerializer.Deserialize<EditorBehaviorData>("{}")!.CodeSyntaxHighlighting, "Older settings default on");
        var saved = SessionStore.Deserialize(SessionStore.Serialize(new SessionData { EditorBehavior = new() { CodeSyntaxHighlighting = false } }));
        Check(saved?.EditorBehavior?.CodeSyntaxHighlighting == false, "Disabled setting survives session serialization");
        var document = new NoteDocument { Path = "example.java" };
        document.Editor.Text = "public class Hello {}";
        document.Refresh();
        var view = new TextEditor(); view.ShowDocument(document);
        var window = new Window { Content = view, Width = 900, Height = 650 };
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        try
        {
            window.Show(); Layout();
            var presenter = document.Editor.GetVisualDescendants().OfType<MarkdownTextPresenter>().Single();
            Avalonia.Media.TextFormatting.TextRunProperties? RunAt(int index)
            {
                var line = presenter.TextLayout.TextLines[presenter.TextLayout.GetLineIndexFromCharacterIndex(index, false)];
                var position = line.FirstTextSourceIndex;
                foreach (var run in line.TextRuns) { if (index >= position && index < position + run.Length) return run.Properties; position += run.Length; }
                return null;
            }
            Color? FirstColor() => (presenter.TextLayout.TextLines[0].TextRuns.First(r => r.Text.Length > 0).Properties?.ForegroundBrush as ISolidColorBrush)?.Color;
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                ThemeService.Apply(theme, persist: false); Layout();
                Check(FirstColor() == ThemeService.Lookup("SyntaxCodeKeyword", theme), theme + " source keyword color");
            }
            var check = EditorFontsFlyout.BuildSyntaxHighlighting();
            check.IsChecked = false; Layout();
            Check(!EditorBehavior.Current.CodeSyntaxHighlighting && FirstColor() == ((ISolidColorBrush)document.Editor.Foreground!).Color, "Settings toggle removes source colors live");
            check.IsChecked = true; Layout();
            Check(FirstColor() == ThemeService.Lookup("SyntaxCodeKeyword", AppTheme.Light), "Settings toggle restores source colors live");
            document.Path = "example.md"; document.Editor.Text = "# Heading\n`code`\n```java\npublic class Hello {}\n```"; document.Refresh(); Layout();
            Check(presenter.Syntax.Spans.Any(s => s.CodeBackground), "Inline code recognized in .md");
            check.IsChecked = false; Layout();
            Check(FirstColor() == ThemeService.Lookup("SyntaxMarker", AppTheme.Light), "Disabling code colors preserves Markdown heading styling");
            var inline = RunAt(document.Text.IndexOf("code", StringComparison.Ordinal));
            Check((inline?.ForegroundBrush as ISolidColorBrush)?.Color == ((ISolidColorBrush)document.Editor.Foreground!).Color
                && inline?.BackgroundBrush is not null, "Disabling inline colors preserves the code background");
            Check((RunAt(document.Text.IndexOf("public", StringComparison.Ordinal))?.ForegroundBrush as ISolidColorBrush)?.Color == ((ISolidColorBrush)document.Editor.Foreground!).Color,
                "Disabling syntax colors also updates Markdown fences");
            check.IsChecked = true; Layout();
            document.Path = "example.markdown"; document.Refresh(); Layout();
            Check(!presenter.StyleInlineCode, "Inline code styling restricted to .md");
            Check(RunAt(document.Text.IndexOf("code", StringComparison.Ordinal))?.BackgroundBrush is null, ".markdown inline code has no code background");
            document.Path = "example.js"; document.Editor.Text = "const s = `code`;"; document.Refresh(); Layout();
            Check(!presenter.HighlightMarkdown && presenter.SourceLanguage == "js" && !presenter.Syntax.Spans.Any(s => s.CodeBackground), "JS backticks are source strings without Markdown inline styling");

            const string markdown = "`inline`\n\n```java\npublic class Hello {}\n```";
            var preview = MarkdownPreview.RenderMapped(MarkdownPreview.Parse(markdown), markdown, null);
            var block = (Border)preview.Controls.Last();
            var runs = ((TextBlock)block.Child!).Inlines!.OfType<Run>().ToList();
            Check(runs.Any(r => r.Text == "public" && r.Foreground is not null), "Preview code uses shared syntax tokens");
            Check(preview.Map.Segments.Any(s => markdown.Substring(s.Start, s.Length) == "public"), "Colored preview tokens keep source mapping");
            var unstyled = MarkdownPreview.RenderMapped(MarkdownPreview.Parse(markdown), markdown, null, styleInlineCode: false);
            Check(((TextBlock)unstyled.Controls.First()).Inlines!.OfType<Run>().Single().Background is null, "Non-.md preview suppresses inline code styling");
            Check(MarkdownExporter.Highlight("public class X {}", "java").Contains("tok-k"), "Export uses Java tokens");
            EditorBehavior.Update(d => d with { CodeSyntaxHighlighting = false }, false);
            Check(!MarkdownExporter.Highlight("public class X {}", "java").Contains("<span"), "Export honors disabled setting");
            preview = MarkdownPreview.RenderMapped(MarkdownPreview.Parse(markdown), markdown, null);
            Check(((TextBlock)((Border)preview.Controls.Last()).Child!).Inlines!.OfType<Run>().All(r => r.Foreground is null), "Disabled preview has no token colors");
        }
        finally { window.Close(); EditorBehavior.Import(null); }
    }
}
