using Avalonia.Threading;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Text;

namespace Mdhub.Views;

public partial class MainView
{
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly HashSet<NoteBuffer> _recoveryBuffers = [];
    private NoteBuffer? _placeholderBuffer;
    private long? _firstRecoveryEdit, _lastRecoveryEdit;
    private bool _restoringRecovery, _checkingExternal, _stopped;
    internal Func<NoteDocument, Task<string?>>? ConflictChoiceOverride { get; set; }

    private void InitializeRecovery()
    {
        _recoveryTimer.Tick += async (_, _) =>
        {
            if (_firstRecoveryEdit is not { } first || _lastRecoveryEdit is not { } last) return;
            var now = Environment.TickCount64;
            if (now - last >= 2000 || now - first >= 10000) await FlushRecoveryAsync();
        };
        AttachedToVisualTree += (_, _) => _recoveryTimer.Start();
        DetachedFromVisualTree += (_, _) => _recoveryTimer.Stop();
    }

    private void TrackRecovery(NoteDocument document)
    {
        if (_recoveryBuffers.Add(document.Buffer)) document.Buffer.Changed += OnRecoveryChanged;
        OnRecoveryChanged(null);
    }

    private void OnRecoveryChanged(NoteDocument? source)
    {
        if (_restoringRecovery || _stopped) return;
        _firstRecoveryEdit ??= Environment.TickCount64;
        _lastRecoveryEdit = Environment.TickCount64;
    }

    public async Task<bool> FlushRecoveryAsync()
    {
        if (_restoringRecovery || _stopped || !SessionManager.Enabled) return true;
        _firstRecoveryEdit = _lastRecoveryEdit = null;
        try { await SessionManager.SaveRecoveryAsync(); return true; }
        catch (Exception)
        {
            OnRecoveryChanged(null); // Retry on the timer; keep the last successful snapshot.
            UpdateStatus(Loc.T("RecoveryFailed"));
            return false;
        }
    }

    public WindowSession CaptureRecoverySession()
    {
        var documents = UniqueDocuments.Where(d => (d.Buffer != _placeholderBuffer || d.IsDirty)
            && (d.Path is not null || !d.IsReadOnlyText)).ToList();
        string Key(NoteDocument d) => d.Path ?? d.Buffer.RecoveryKey;
        return CaptureSession() with
        {
            Tabs = documents.Select(Key).ToList(),
            ActiveTab = _active is null ? -1 : documents.FindIndex(d => d.Buffer == _active.Buffer),
            Recovery = documents.Where(d => !d.IsViewOnly && !d.IsReadOnlyText && (d.IsDirty || d.Path is null)).Select(d => new RecoveryDocument
            {
                Key = Key(d), Path = d.Path, Name = d.UntitledName, Text = d.Text, SavedText = d.SavedText,
                CodePage = d.Encoding.CodePage, Bom = d.Encoding.GetPreamble().Length > 0,
                SavedCodePage = d.Buffer.SavedEncoding.CodePage, SavedBom = d.Buffer.SavedEncoding.GetPreamble().Length > 0,
                LineEnding = (int)d.LineEnding, SavedLineEnding = (int)d.SavedLineEnding
            }).ToList()
        };
    }

    private NoteDocument RestoreRecoveryDocument(RecoveryDocument recovery)
    {
        var document = AddDocument(recovery.Path, recovery.Text, recovery.Name, recovery.CreateEncoding());
        document.SavedText = recovery.SavedText;
        document.Buffer.SavedEncoding = (recovery with { CodePage = recovery.SavedCodePage ?? recovery.CodePage, Bom = recovery.SavedBom ?? recovery.Bom }).CreateEncoding();
        document.LineEnding = (LineEnding)recovery.LineEnding;
        document.SavedLineEnding = (LineEnding)recovery.SavedLineEnding;
        return document;
    }

    // Posted so opening/restoring a tab can finish before a modal conflict dialog is shown.
    public void ScheduleExternalCheck()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (_busy || _checkingExternal || _restoringRecovery || _stopped || _active is null) return;
            await RunAsync(() => CheckExternalChangeAsync(_active), focusEditor: false);
        }, DispatcherPriority.Background);
    }

    internal async Task CheckExternalChangeAsync(NoteDocument document)
    {
        if (_checkingExternal || document.IsViewOnly || document.IsReadOnlyText || document.Path is not { } path) return;
        _checkingExternal = true;
        try
        {
            var missing = !File.Exists(path);
            var loaded = missing ? (Text: "", Encoding: document.Encoding) : await TextFileStore.ReadAsync(path);
            if (!missing && loaded.Text == document.SavedText
                && loaded.Encoding.CodePage == document.Buffer.SavedEncoding.CodePage
                && loaded.Encoding.GetPreamble().SequenceEqual(document.Buffer.SavedEncoding.GetPreamble())) return;
            if (_active?.Buffer != document.Buffer) return;
            var choice = !document.IsDirty && !missing ? "Reload" : await (ConflictChoiceOverride?.Invoke(document)
                ?? DialogService.ShowAsync(Host, Loc.T("FileChangedTitle"), Loc.F(missing ? "FileDeletedMessage" : "FileChangedMessage", path), "ReplaceDiskFile", "Reload", "Cancel"));
            if (choice == "ReplaceDiskFile") await SaveAsync(document, false);
            else if (choice == "Reload" && !missing)
            {
                // Read again: the file may have changed while the dialog was open.
                loaded = await TextFileStore.ReadAsync(path);
                document.Editor.IsUndoEnabled = false;
                document.Editor.Text = loaded.Text;
                document.Editor.IsUndoEnabled = true;
                document.Encoding = loaded.Encoding;
                document.Buffer.SavedEncoding = loaded.Encoding;
                document.SavedText = loaded.Text;
                document.SetLoadedLineEnding(LineEndings.Detect(loaded.Text, LineEndingSettings.ForNewFile));
                document.Buffer.ResetHistory();
                document.Refresh();
                await FlushRecoveryAsync();
            }
            else if (choice == "Reload" && missing) UpdateStatus(Loc.T("FileMissingReload"));
        }
        finally { _checkingExternal = false; }
    }
}
