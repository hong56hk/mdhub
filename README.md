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
