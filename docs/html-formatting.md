# Basic HTML formatting

Editor Settings → **Enable HTML formatting** is on by default and saved with
the editor settings. Turning it off shows HTML source in the preview and hides
the extra HTML toolbar buttons. It does not change your files.

Markdown files can mix Markdown and inline HTML. Block HTML follows the Markdown
parser's boundaries and the [Markdown Guide](https://www.markdownguide.org/basic-syntax/#html):
put blank lines around HTML blocks, and use HTML formatting inside them.

```md
This **word** is bold; this <em>word</em> is italic.

<div style="text-align: center; color: #1565c0;">
  <p>Use <strong>HTML bold</strong> here. **This stays literal.**</p>
</div>
```

HTML in code spans, fenced code blocks, and escaped tags remains literal.
`.html` and `.htm` files use the same limited renderer without Markdown parsing.

Supported elements:

- Containers: `div`, `p`, `blockquote`, `pre`, `h1`–`h6`, `hr`.
- Inline formatting: `span`, `strong`, `b`, `em`, `i`, `u`, `s`, `del`,
  `sup`, `sub`, `code`, `br`, `a` (with `href`).
- Lists: `ol`, `ul`, `li`, including nested lists, `ol start` and `li value`.
- Tables: `table`, `caption`, `thead`, `tbody`, `tfoot`, `tr`, `th`, `td`,
  including `colspan` and `rowspan` (bounded to 100).
- Images: `img` with `src`, `alt`, numeric `width` and `height` (bounded to 4096 px).
  Local paths resolve relative to the document; remote or missing images show
  alternative text, consistent with the existing offline Markdown preview.
- Styles: `text-align: left|center|right` and `color` (named colors or hex).
  The `align` attribute also works. These styles inherit through containers.
- HTML entities and comments. Comments are hidden while formatting is enabled.

The additional toolbar offers alignment, underline, superscript, subscript,
line break, a `div` container, HTML table, text color, and an image with width.
Alignment wraps the selection or current paragraph in a `div`. Existing Markdown
inside that new block stays literal; it is not converted automatically. Bold,
italic, strike, code, heading, list, quote and link commands use HTML syntax when
the selection is inside a raw HTML block or an HTML file. Otherwise the existing
Markdown commands keep their usual behavior. All insertions can be undone.

This is a native document preview, not a web browser: stylesheets, JavaScript,
event handlers, forms, embedded pages, and web layouts are not implemented.
Unsupported tags are displayed literally, and unsupported attributes/styles are
ignored. HTML blocks support preview selection/copy and source editing; editing
rendered HTML text directly in the preview is not supported.
