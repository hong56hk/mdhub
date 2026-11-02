# Code syntax highlighting

Open the editor gear and toggle **Code syntax highlighting**. It is enabled by default, saved with your settings, and updates open editors and previews immediately. Turning it off removes code text colors while keeping code fonts, backgrounds, and other Markdown formatting. HTML/PDF exports honor the option too.

Source files select their language by extension. Markdown fenced code blocks select it by the language after the opening fence, for example `python`, `cpp`, `csharp`, `java`, or `mongodb`. Unsupported languages remain plain text.

| Language / format | Source extensions | Common fence labels |
|---|---|---|
| Python | `.py`, `.pyw` | `python`, `py`, `python3` |
| C | `.c`, `.h` | `c` |
| C++ | `.cpp`, `.cc`, `.cxx`, `.hpp`, `.hh`, `.hxx` | `cpp`, `c++` |
| C# | `.cs` | `csharp`, `cs`, `c#` |
| F# | `.fs`, `.fsx` | `fsharp`, `fs`, `f#` |
| Visual Basic .NET | `.vb` | `vb`, `vbnet`, `visualbasic` |
| Java | `.java` | `java` |
| Swift | `.swift` | `swift` |
| Go | `.go` | `go`, `golang` |
| JavaScript | `.js`, `.mjs`, `.cjs`, `.jsx` | `javascript`, `js`, `jsx` |
| TypeScript | `.ts`, `.tsx` | `typescript`, `ts`, `tsx` |
| CSS | `.css` | `css` |
| HTML | `.html`, `.htm` | `html`, `htm` |
| SQL | `.sql` | `sql`, `mysql`, `postgresql`, `sqlite`, `tsql` |
| JSON | `.json`, `.jsonc`, `.json5` | `json`, `jsonc`, `json5` |
| MongoDB scripts | `.js` (JavaScript) | `mongo`, `mongodb`, `mongosh` |
| Shell | `.sh`, `.bash`, `.zsh` | `bash`, `sh`, `shell`, `zsh` |
| PowerShell | `.ps1`, `.psm1`, `.psd1` | `powershell`, `pwsh`, `ps1` |
| YAML | `.yaml`, `.yml` | `yaml`, `yml` |
| XML / XAML | `.xml`, `.xaml`, `.axaml`, `.svg`, `.csproj` | `xml`, `xaml`, `svg` |

.NET language support includes C#, F#, and Visual Basic. MongoDB query scripts use JavaScript rules; JSON documents use JSON rules. JSX/TSX use the JavaScript/TypeScript tokenizer.

Inline code in backticks gets a distinct text color, code font, and subtle background only in `.md` files. Source-language backticks, such as JavaScript template strings, follow their language's string rules.

HTML tags embedded directly in Markdown also highlight tag names, attributes, quoted values, and single-line comments using the HTML colors. Text between tags keeps its Markdown formatting.

The highlighter runs offline and uses the app's light/dark theme colors. It provides lexical highlighting, rather than compiler diagnostics or semantic analysis.
