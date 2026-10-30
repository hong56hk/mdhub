using System.Diagnostics;
using System.Net;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Mdhub.Services;

// Item 51: export a Markdown note to a standalone, offline HTML file (Light preview look, images embedded) and
// to PDF by printing that HTML with a locally installed Chromium browser (Edge on Windows) in headless mode.
public static class MarkdownExporter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseGridTables().UseEmphasisExtras().UseTaskLists().UseAutoLinks().UseFootnotes().UseAutoIdentifiers().Build();

    // The Light theme colours (read from Styles/Theme.axaml, so the export always matches the Light preview and code colours).
    private static readonly string[] ColourKeys =
    [
        "PreviewBackground", "TextPrimary", "CodeBlockBackground", "QuoteBorder", "TableBorder", "TableHeaderBackground", "RuleForeground",
        "InlineCodeBackground", "InlineCodeForeground", "LinkForeground", "SyntaxCodeKeyword", "SyntaxCodeString", "SyntaxCodeComment", "SyntaxCodeNumber",
    ];
    public static IReadOnlyDictionary<string, string> LightColours => ColourKeys.ToDictionary(k => k, k =>
    {
        var c = ThemeService.Lookup(k, AppTheme.Light);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    });

    public static string Css()
    {
        var c = LightColours;
        // Item 35: the preview's fonts and spacing (gear flyout).
        var p = PreviewSettings.Current;
        string Px(double v) => PreviewSettings.Css(v) + "px";
        var k = p.BodySize / PreviewSettings.DefaultBodySize;
        var bodyFamily = p.BodyFamily is { } chosen ? PreviewSettings.CssFamily(chosen) + ", " : "";
        var codeFamily = p.CodeFamily == PreviewSettings.DefaultCodeFamily ? "" : PreviewSettings.CssFamily(p.CodeFamily) + ", ";
        // (fonts use single quotes: CSS family names, not UI text)
        return $$"""
            :root { color-scheme: light; }
            html { background: {{c["PreviewBackground"]}}; }
            body { margin: 0 auto; max-width: 860px; padding: 32px 40px; color: {{c["TextPrimary"]}}; background: {{c["PreviewBackground"]}};
              font-family: {{bodyFamily}}'Segoe UI', system-ui, -apple-system, 'Helvetica Neue', Arial, 'Microsoft JhengHei', 'Yu Gothic UI', 'Malgun Gothic', sans-serif;
              font-size: {{Px(p.BodySize)}}; line-height: {{Px(PreviewSettings.BodyLineHeight)}}; overflow-wrap: break-word; }
            h1, h2, h3, h4, h5, h6 { font-weight: 600; margin: 10px 0 4px; break-after: avoid; page-break-after: avoid; }
            h1 { font-size: {{Px(28 * k)}}; line-height: {{Px(42 * k)}}; } h2 { font-size: {{Px(23 * k)}}; line-height: {{Px(34 * k)}}; } h3 { font-size: {{Px(19 * k)}}; line-height: {{Px(28 * k)}}; } h4, h5, h6 { font-size: {{Px(16 * k)}}; }
            p, ul, ol, blockquote, pre, table { margin: 0 0 {{Px(p.ParagraphSpacing)}}; }
            a { color: {{c["LinkForeground"]}}; text-decoration: none; } a:hover { text-decoration: underline; }
            code { font-family: {{codeFamily}}Consolas, Menlo, 'Cascadia Mono', monospace; font-size: {{Px(p.CodeSize)}}; background: {{c["InlineCodeBackground"]}}; color: {{c["InlineCodeForeground"]}}; padding: 1px 4px; border-radius: 3px; }
            pre { background: {{c["CodeBlockBackground"]}}; border-radius: 5px; padding: 14px; line-height: {{Px(Math.Round(p.CodeSize * 20 / PreviewSettings.DefaultCodeSize, 1))}}; white-space: pre-wrap; break-inside: avoid; page-break-inside: avoid; }
            pre code { background: none; color: inherit; padding: 0; font-size: {{Px(p.CodeSize)}}; }
            blockquote { border-left: 3px solid {{c["QuoteBorder"]}}; padding: 4px 14px; margin-left: 0; break-inside: avoid; }
            table { border-collapse: collapse; }
            th, td { border: 0.5px solid {{c["TableBorder"]}}; padding: 10px; text-align: left; vertical-align: top; }
            th { background: {{c["TableHeaderBackground"]}}; }
            tr { break-inside: avoid; page-break-inside: avoid; }
            thead { display: table-header-group; }
            hr { border: 0; height: 1px; background: {{c["RuleForeground"]}}; margin: 8px 0; }
            img { max-width: 100%; break-inside: avoid; }
            ul.contains-task-list { list-style: none; padding-left: 4px; } li.task-list-item input { margin-right: 8px; }
            li + li { margin-top: {{Px(PreviewSettings.NestedSpacing)}}; }
            ol > li::marker { font-size: 1.1em; } ul > li::marker { font-size: 1.15em; }
            .tok-k { color: {{c["SyntaxCodeKeyword"]}}; } .tok-s { color: {{c["SyntaxCodeString"]}}; } .tok-c { color: {{c["SyntaxCodeComment"]}}; font-style: italic; } .tok-n { color: {{c["SyntaxCodeNumber"]}}; }
            @page { size: A4; margin: 18mm 16mm; }
            @media print { html, body { background: white; } body { max-width: none; padding: 0; } }
            """;
    }

    // baseDirectory resolves relative image paths (null for an untitled note: only absolute paths embed).
    public static string ToHtml(string markdown, string title, string? baseDirectory)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
            if (link.IsImage && EmbedImage(link.Url, baseDirectory) is { } data) link.Url = data;
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        var old = renderer.ObjectRenderers.FindExact<CodeBlockRenderer>();
        if (old is not null) renderer.ObjectRenderers.Remove(old);
        renderer.ObjectRenderers.Insert(0, new HighlightedCodeRenderer());
        renderer.Render(document);
        return $"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="generator" content="mdhub">
            <title>{WebUtility.HtmlEncode(title)}</title>
            <style>
            {Css()}
            </style>
            </head>
            <body>
            {writer}
            </body>
            </html>
            """;
    }

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml", [".bmp"] = "image/bmp", [".ico"] = "image/x-icon", [".avif"] = "image/avif",
    };

    // A local image becomes a data: URI; web, data and missing images are left as they are.
    public static string? EmbedImage(string? url, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        string path;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme.Length > 1)
        {
            if (!uri.IsFile) return null;
            path = uri.LocalPath;
        }
        else
        {
            var decoded = Uri.UnescapeDataString(url.Split('#', '?')[0]);
            if (Path.IsPathRooted(decoded)) path = decoded;
            else if (baseDirectory is null) return null;
            else path = Path.GetFullPath(Path.Combine(baseDirectory, decoded));
        }
        if (!ImageTypes.TryGetValue(Path.GetExtension(path), out var mime) || !File.Exists(path)) return null;
        return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }

    // ---- PDF ----
    // Test hook: replaces browser discovery.
    public static Func<string?>? BrowserOverride { get; set; }

    public static string? FindBrowser()
    {
        if (BrowserOverride is not null) return BrowserOverride();
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
            foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
                if (!string.IsNullOrEmpty(root))
                {
                    candidates.Add(Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe"));
                    candidates.Add(Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe"));
                }
        if (OperatingSystem.IsMacOS())
            candidates.AddRange(["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge", "/Applications/Chromium.app/Contents/MacOS/Chromium"]);
        if (OperatingSystem.IsLinux())
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
                foreach (var name in new[] { "microsoft-edge", "google-chrome", "chromium", "chromium-browser" })
                    candidates.Add(Path.Combine(dir, name));
        return candidates.FirstOrDefault(File.Exists);
    }

    public static async Task ToPdfAsync(string html, string pdfPath, CancellationToken cancel = default)
    {
        var browser = FindBrowser() ?? throw new IOException(Mdhub.Localization.Loc.T("ExportNoBrowser"));
        var work = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-export-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var htmlPath = Path.Combine(work, "note.html");
            await File.WriteAllTextAsync(htmlPath, html, new UTF8Encoding(false), cancel);
            var output = Path.Combine(work, "note.pdf");
            var info = new ProcessStartInfo(browser) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--no-pdf-header-footer",
                         "--run-all-compositor-stages-before-draw", $"--user-data-dir={Path.Combine(work, "profile")}", $"--print-to-pdf={output}", new Uri(htmlPath).AbsoluteUri })
                info.ArgumentList.Add(arg);
            Process process;
            try { process = Process.Start(info) ?? throw new IOException(Mdhub.Localization.Loc.T("ExportPdfFailed")); }
            catch (System.ComponentModel.Win32Exception) { throw new IOException(Mdhub.Localization.Loc.T("ExportNoBrowser")); }
            using (process)
            {
                _ = process.StandardOutput.ReadToEndAsync(cancel); _ = process.StandardError.ReadToEndAsync(cancel);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(TimeSpan.FromSeconds(90));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch { } throw new IOException(Mdhub.Localization.Loc.T("ExportPdfFailed")); }
            }
            if (!File.Exists(output) || new FileInfo(output).Length < 5 || !File.ReadAllBytes(output).AsSpan(0, 5).SequenceEqual("%PDF-"u8))
                throw new IOException(Mdhub.Localization.Loc.T("ExportPdfFailed"));
            File.Copy(output, pdfPath, overwrite: true);
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    // ---- Code highlighting: a small offline tokenizer (keywords, strings, comments, numbers). ----
    private sealed class HighlightedCodeRenderer : HtmlObjectRenderer<CodeBlock>
    {
        protected override void Write(HtmlRenderer renderer, CodeBlock block)
        {
            var language = (block as FencedCodeBlock)?.Info?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
            var lines = block.Lines.Lines ?? [];
            var code = string.Join("\n", Enumerable.Range(0, block.Lines.Count).Select(i => lines[i].Slice.ToString()));
            renderer.Write("<pre><code");
            if (!string.IsNullOrEmpty(language)) renderer.Write($" class=\"language-{WebUtility.HtmlEncode(language)}\"");
            renderer.Write(">").Write(Highlight(code, language)).WriteLine("</code></pre>");
        }
    }

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract","and","as","async","await","break","case","catch","class","const","continue","def","default","del","delete","do","elif","else","enum","except","export",
        "extends","false","final","finally","fn","for","foreach","from","func","function","go","if","impl","import","in","interface","internal","is","lambda","let","match",
        "mod","namespace","new","nil","None","not","null","or","override","package","pass","private","protected","pub","public","raise","readonly","record","ref",
        "return","sealed","select","self","static","struct","super","switch","this","throw","trait","true","True","False","try","type","typeof","use","using","var","void",
        "while","with","yield","int","string","bool","float","double","long","char","byte","object","echo","fi","then","done","esac",
    };
    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    { "select","from","where","insert","update","delete","join","left","right","inner","outer","on","order","by","group","having","values","into","create","table","and","or","not","null","as","limit" };
    private static readonly HashSet<string> HashComment = new(StringComparer.OrdinalIgnoreCase) { "py", "python", "sh", "bash", "zsh", "shell", "ps1", "powershell", "pwsh", "yaml", "yml", "toml", "ruby", "rb", "r", "perl", "makefile", "dockerfile", "ini", "conf" };
    private static readonly HashSet<string> NoHighlight = new(StringComparer.OrdinalIgnoreCase) { "text", "txt", "plain", "plaintext", "md", "markdown" };

    public static string Highlight(string code, string? language)
    {
        if (string.IsNullOrEmpty(language) || NoHighlight.Contains(language)) return WebUtility.HtmlEncode(code);
        var hash = HashComment.Contains(language);
        var sql = language is "sql";
        var markup = language is "html" or "xml" or "xaml" or "svg" or "axaml";
        var slash = !hash && !sql && !markup;
        var sb = new StringBuilder();
        void Span(string cls, string text) => sb.Append($"<span class=\"tok-{cls}\">").Append(WebUtility.HtmlEncode(text)).Append("</span>");
        int End(string close, int from) { var e = code.IndexOf(close, from, StringComparison.Ordinal); return e < 0 ? code.Length : e + close.Length; }
        var i = 0;
        while (i < code.Length)
        {
            var ch = code[i];
            var next = i + 1 < code.Length ? code[i + 1] : '\0';
            if (markup && code.AsSpan(i).StartsWith("<!--")) { var e = End("-->", i + 4); Span("c", code[i..e]); i = e; continue; }
            if ((hash && ch == '#') || (slash && ch == '/' && next == '/') || (sql && ch == '-' && next == '-'))
            { var e = code.IndexOf('\n', i); if (e < 0) e = code.Length; Span("c", code[i..e]); i = e; continue; }
            if ((slash || sql) && ch == '/' && next == '*') { var e = End("*/", i + 2); Span("c", code[i..e]); i = e; continue; }
            if (ch is '"' or '\'' or '`')
            {
                var e = i + 1;
                while (e < code.Length && code[e] != ch && !(code[e] == '\n' && ch != '`')) e += code[e] == '\\' ? 2 : 1;
                e = Math.Min(code.Length, e + 1);
                Span("s", code[i..e]); i = e; continue;
            }
            if (char.IsDigit(ch) && (i == 0 || !char.IsLetterOrDigit(code[i - 1]) && code[i - 1] != '_'))
            { var e = i; while (e < code.Length && (char.IsLetterOrDigit(code[e]) || code[e] is '.' or '_')) e++; Span("n", code[i..e]); i = e; continue; }
            if (char.IsLetter(ch) || ch == '_')
            {
                var e = i; while (e < code.Length && (char.IsLetterOrDigit(code[e]) || code[e] == '_')) e++;
                var word = code[i..e];
                if (!markup && (sql ? SqlKeywords.Contains(word) : Keywords.Contains(word))) Span("k", word);
                else sb.Append(WebUtility.HtmlEncode(word));
                i = e; continue;
            }
            sb.Append(WebUtility.HtmlEncode(ch.ToString()));
            i++;
        }
        return sb.ToString();
    }
}