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
- Folder browsing lists all files, including hidden files; select `.txt`, `.md`, or `.markdown` files to edit. Subfolders load on expansion. Linked folders are listed but not traversed. Use View → Refresh Folder after external changes.
- The opened-folder row has New File, New Folder, Reload, and Collapse All buttons. Creation uses the selected explorer item's parent folder (including a selected folder's parent), otherwise the active saved file's folder, otherwise the opened root. The name dialog shows the destination and rejects invalid or duplicate names. Newly created files open in a tab; nested items are revealed in the tree. Reload retains the existing refresh behavior; Collapse All collapses every loaded folder.
- Tabs retain their own unsaved text, selection, and undo history. Opening the same path selects its existing tab.
- Markdown toolbar actions insert source syntax. Plain-text files keep literal text and disable Markdown formatting.
- Offline preview of headings, emphasis, strikethrough, lists, quotes, code blocks, links as styled text, and tables.
- Live counts: whitespace-delimited words, Unicode text-element characters (including whitespace), and logical lines. An empty document has one line.
- Plain-text editing with selection, copy/paste, undo/redo, and scrolling.
- Ctrl+N, Ctrl+O, Ctrl+Shift+O (folder), Ctrl+S, Ctrl+Shift+S, and Ctrl+W (Command on macOS).
- Unsaved-change indicator and Save / Discard / Cancel prompts before closing tabs or exiting.
- UTF-8 text and BOM-marked Unicode files; detected encoding is retained when saving. Legacy non-UTF-8 files without a BOM are not supported.
- Writes use a temporary file and replacement to avoid truncating the original on a failed write.

This is an initial workspace, not yet the full note organizer. It does not detect changes made by another app while a file is open. Images appear as alt-text labels; HTML is literal text, links do not launch a browser, and math/Mermaid diagrams are not rendered. No preview content is fetched from the network.

## Publish for Windows

```powershell
dotnet publish Mdhub.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

Distribute the entire output folder. Users do not need to install .NET. macOS requires its own build/package and platform testing.

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
