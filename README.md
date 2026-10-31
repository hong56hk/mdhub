<p align="center">
  <img src="docs/logo.png" alt="MDHub logo" width="96">
</p>

<h1 align="center">MDHub</h1>

<p align="center">
  A fast, offline desktop workspace for organizing and writing Markdown notes.<br>
  Built with C#, .NET 10 and Avalonia.
</p>

> **Status: alpha.** MDHub is under active development. Expect rough edges, and back up your notes.

## Why MDHub

If you keep your notes, journals or docs as plain `.md` and `.txt` files in folders, MDHub gives you one place to browse, search and edit them, with a live preview, without locking your files into a database or a cloud service. Your notes stay ordinary files on your disk or NAS.

## Features

- **Folder workspace:** open any folder and browse every file in a tree. Create, rename (F2), move by drag and drop, and reveal files.
- **Markdown editor with live preview:** side-by-side source and formatted preview, a formatting toolbar, syntax highlighting, and line numbers.
- **Quick Open (Ctrl+P):** jump to any file by typing part of its name or path; add `:42` to go to a line.
- **Global search and replace (Ctrl+Shift+F / Ctrl+Shift+H):** search across every file with case, whole-word and regex options, and replace in one file or all.
- **Tabs that remember:** each tab keeps its own unsaved text, selection and undo history, and your session is restored next time.
- **Text encodings:** UTF-8, Unicode and legacy encodings such as Big5 open correctly and are saved back in their original encoding.
- **Image viewer:** open images inline, zoom with the mouse wheel and drag to pan.
- **Themes and languages:** dark and light themes; English, 繁體中文, 日本語 and 한국어.
- **Custom shortcuts:** change any keyboard shortcut, with reset to defaults.
- **Safe saving:** files are written through a temporary file, so a failed save never truncates your note.
- **Works offline:** nothing is sent over the network.

## Download

- New, Open, Save, and Save As, with native file dialogs.
- Dark workspace with File / Edit / View / Help menus, file tabs, and a formatting toolbar.
- Custom title bar: minimize, maximize/restore, and close are at the right of the top menu. Drag the empty menu-bar space to move the window; double-click it to maximize/restore. The native resize border and unsaved-change close prompts remain available.
- Three resizable columns: opened-folder tree, source editor, and live formatted preview.
- Folder browsing lists all files, including hidden files. Markdown uses the source editor and formatted pane; other text files (including source code, JSON, CSV, and extensionless files) use a full-width text editor with formatting buttons disabled. Binary files are rejected. Subfolders load on expansion; linked folders are listed but not traversed.
- Images open in a fitted, view-only image viewer across the content area. Image tabs show pixel dimensions and retain their image when switching tabs. Save does not modify image files.
- The opened-folder row has New File, New Folder, Reload, and Collapse All buttons. Creation uses the selected explorer item's parent folder (including a selected folder's parent), otherwise the active saved file's folder, otherwise the opened root. The name dialog shows the destination and rejects invalid or duplicate names. Newly created files open in a tab; nested items are revealed in the tree. Reload retains the existing refresh behavior; Collapse All collapses every loaded folder.
- Tabs retain their own unsaved text, selection, and undo history. Opening the same path selects its existing tab.
- The sidebar has EXPLORER (Ctrl+Shift+E) and SEARCH (Ctrl+Shift+F) tabs. Ctrl+Shift+H opens folder-wide replace. Search recursively groups matches by file, highlights matching text, supports case/whole-word/regex options, and includes unsaved text in open files. Click a result to select its text in the editor.
- Search replacement can target a match, a file, or all results, with optional preserve-case. Open files keep replacements unsaved and undoable; closed files are saved with their original encoding. Changed files are skipped. Search excludes `.git`, internal metadata, linked items, and binary files; files over 5 MB are skipped and results are capped at 5,000, with limits reported in the panel. Replace all is disabled for truncated results.
- Rename an explorer item with F2 (while the explorer has focus) or its Rename context-menu action. The name is edited in place, with the file extension excluded from the initial selection (folders, extensionless names and dotfiles select the whole name). Enter or clicking elsewhere confirms; Escape cancels. Empty, invalid or duplicate names show an error and remain editable.
- Drag files within the explorer onto a folder to move them. Hover over a collapsed folder to expand it. Drop onto the opened folder name or empty explorer space to move a file back to the root. Open tabs and unsaved edits follow the moved file; existing files are never overwritten.
- Markdown toolbar actions insert source syntax. Plain-text files keep literal text and disable Markdown formatting.
- The Markdown source editor colors heading markers purple, numbered-list markers blue, and bullet markers green. Code blocks and plain-text files keep their normal text color.
- Offline preview of headings, emphasis, strikethrough, lists, quotes, code blocks, links as styled text, and tables.
- Live counts: whitespace-delimited words, Unicode text-element characters (including whitespace), and logical lines. An empty document has one line.
- Plain-text editing with selection, copy/paste, undo/redo, and scrolling.
- Ctrl+N, Ctrl+O, Ctrl+Shift+O or Ctrl+Shift+N (folder), Ctrl+S, Ctrl+Shift+S, and Ctrl+W (Command on macOS).
- Unsaved-change indicator and Save / Discard / Cancel prompts before closing tabs or exiting.
- UTF-8 text and BOM-marked Unicode files; detected encoding is retained when saving. Legacy non-UTF-8 files without a BOM are not supported.
- Writes use a temporary file and replacement to avoid truncating the original on a failed write.

This is an initial workspace, not yet the full note organizer. It does not detect changes made by another app while a file is open. Images appear as alt-text labels; HTML is literal text, links do not launch a browser, and math/Mermaid diagrams are not rendered. No preview content is fetched from the network.

## Publish

`.github/workflows/release.yml` publishes self-contained builds for `win-x64`, `osx-arm64`, and `osx-x64`. Pushing a `v*` tag runs it and attaches the archives to a GitHub Release. `workflow_dispatch` runs the same publish and uploads the archives as workflow artifacts, without creating a release.

| Archive | Contents |
| --- | --- |
| `mdhub-win-x64.zip` | `mdhub-win-x64/Mdhub.exe` and its dependencies |
| `mdhub-osx-arm64.zip` | unsigned `Mdhub.app` for Apple silicon |
| `mdhub-osx-x64.zip` | unsigned `Mdhub.app` for Intel Macs |

Users do not need to install .NET. The macOS bundles are not signed or notarized, so Gatekeeper blocks them until a follow-up adds Developer ID signing (hardened runtime, including `com.apple.security.cs.allow-jit`) and notarization.

To pack the same archives locally:

```bash
GITHUB_REF=refs/tags/v1.2.3 bash scripts/package-release.sh --rid win-x64 --version 1.2.3
bash scripts/package-release.sh --rid osx-arm64 --version 1.2.3
bash scripts/package-release.sh --rid osx-x64 --version 1.2.3
```
Get the latest build from the [Releases](https://github.com/hong56hk/mdhub/releases) page:

- **Windows (x64):** `MDHub.exe`, a single file with nothing to install.
- **macOS:** `.dmg` for Apple Silicon or Intel.

Builds aren't code-signed yet, so your system will warn the first time you open MDHub:

- **Windows:** in the SmartScreen dialog, click **More info**, then **Run anyway**.
- **macOS:** right-click the app and choose **Open**, or go to **System Settings > Privacy & Security** and click **Open Anyway**.

### Verify a download

Every release file is built by GitHub Actions from this public source and carries a GitHub build attestation. To confirm a file is unmodified and came from this repo:

```bash
gh attestation verify MDHub.exe --repo hong56hk/mdhub
```

## Build from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then:

```bash
git clone https://github.com/hong56hk/mdhub
cd mdhub
dotnet run --project Mdhub.csproj
```

Run the tests:

```bash
dotnet run --project tests/Mdhub.FileTests.csproj
dotnet run --project tests/Ui/Mdhub.UiTests.csproj
```

To debug in VS Code, install the C# extension (`ms-dotnettools.csharp`), select **Debug mdhub** in Run and Debug, and press F5.

## Project structure

```text
Views/         The main workspace view and its wiring
Components/    UI pieces: explorer, tabs, editor, preview, search, Quick Open...
Services/      File, folder, search, session, theme and shortcut services
Text/          Formatting and statistics, independent of the UI
Rendering/     Markdown (Markdig) rendering into native controls
Localization/  UI strings for each language
tests/         File and headless UI tests
```

## License

MDHub is released under the [MIT License](LICENSE).
