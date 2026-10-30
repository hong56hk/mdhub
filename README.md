# mdhub

A local text and Markdown workspace built with C#, .NET 10, Avalonia, and Markdig.

## Run

Install the .NET 10 SDK, then run from this folder:

```powershell
dotnet run --project Mdhub.csproj
```

The first build restores NuGet dependencies using the internet. The built application runs offline.

## Debug in VS Code

Open this folder in VS Code and install the recommended Microsoft C# extension (`ms-dotnettools.csharp`). Select **Debug mdhub** in Run and Debug, then press **F5**. The launch configuration builds the project in Debug mode before starting it. Set breakpoints in the C# source files as usual.

## Features

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

## Structure

Run the dependency-free file persistence, formatting, and statistics checks:

```powershell
dotnet run --project tests/Mdhub.FileTests.csproj
```

Run headless UI checks (tabs, formatting, preview, and screenshot generation):

```powershell
dotnet run --project tests/Ui/Mdhub.UiTests.csproj
```

Close a running debug instance before rebuilding the same executable. UI tests write a screenshot to `artifacts/layout-preview.png`.

```text
MainWindow.axaml              Application window hosting the current full view
Views/
  MainView.axaml              Whole workspace: composes the seven components
  MainView.axaml.cs           Component wiring, active state, busy/close lifecycle
  MainView.Documents.cs      Open/save/tab workflows and unsaved-change decisions
  MainView.Actions.cs        Menu/keyboard routing and panel layout actions
Components/                  Each component has .axaml and .axaml.cs files
  TopMenu                    File / Edit / View / Help
  FileTabs                   Open tabs, selection, new and close requests
  TextFormatter              Markdown formatting toolbar
  FolderExplorer             Folder tree, lazy expansion, file selection
  TextEditor                 Source editor and editing/formatting operations
  MdEditor                   Existing formatted Markdown/plain-text preview
  BottomStatusBar            Counts, document status, and encoding
Models/
  NoteDocument.cs            Per-tab document and editor state
Services/
  TextFileStore.cs           Encoding and safe file persistence
  FolderService.cs           Directory enumeration
  DialogService.cs           Owned confirmation/error dialogs
Text/                        UI-independent formatting and statistics
Rendering/                   Markdown syntax-tree rendering into native controls
Styles/                      Shared workspace styles
```

A **view** is a complete page, while a **component** is a piece of a page. Components expose events and focused methods; they do not find or reference `MainWindow` or `MainView`. The main view coordinates operations between them, and the window forwards only title, focus, and close events. The main view's partial code files share one workspace state rather than duplicating it across components.

Future search or settings pages belong under `Views/` and can reuse components from `Components/`. Add navigation to the window host when another view is actually introduced; no empty placeholder views or extra navigation dependencies are included now.

`MdEditor` is the existing read-only formatted pane, not a new rich-text editor. `NoteDocument` deliberately retains its own Avalonia text control to preserve each tab's undo history, caret, and selection. This is a code-behind composition approach, with no added MVVM framework or runtime dependency.
