using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Ampm2.Pm2;
using Ampm2.Sys;
using Microsoft.Win32;

namespace Ampm2.Ui;

/// <summary>The Saved list: ampm2's own copy of the process list, kept in sync with pm2 and importable/exportable.</summary>
public sealed partial class MainViewModel
{
    private AppLibrary _library = AppLibrary.Load();
    public ObservableCollection<SavedApp> SavedItems { get; } = new();

    public ICommand ShowProcessesCommand { get; private set; } = null!;
    public ICommand ShowSavedCommand { get; private set; } = null!;
    public ICommand SyncSavedNowCommand { get; private set; } = null!;
    public ICommand StartSavedCommand { get; private set; } = null!;
    public ICommand StartMissingCommand { get; private set; } = null!;
    public ICommand RemoveSavedCommand { get; private set; } = null!;
    public ICommand ImportSavedCommand { get; private set; } = null!;
    public ICommand ExportSavedCommand { get; private set; } = null!;
    public ICommand SaveDefinitionCommand { get; private set; } = null!;
    public ICommand RevertDefinitionCommand { get; private set; } = null!;
    public ICommand OpenSavedFileCommand { get; private set; } = null!;

    private void InitSaved()
    {
        ShowProcessesCommand = new RelayCommand(() => ViewMode = "processes");
        ShowSavedCommand = new RelayCommand(() => ViewMode = "saved");
        SyncSavedNowCommand = new RelayCommand(() =>
        {
            int n = SyncLibrary(force: true);
            App.Toast("Saved list updated", n == 0 ? "Already up to date." : $"{n} app{(n == 1 ? "" : "s")} saved from pm2.", ToastKind.Success);
        }, () => IsConnected && Items.Count > 0);
        StartSavedCommand = new AsyncCommand(p => StartSavedAsync(SavedTargets(p).Where(s => s.IsMissing).ToList()),
            p => CanUseCli && SavedTargets(p).Any(s => s.IsMissing));
        StartMissingCommand = new AsyncCommand(() => StartSavedAsync(SavedItems.Where(s => s.IsMissing).ToList()),
            () => CanUseCli && SavedItems.Any(s => s.IsMissing));
        RemoveSavedCommand = new AsyncCommand(p => RemoveSavedAsync(SavedTargets(p)), p => SavedTargets(p).Count > 0);
        ImportSavedCommand = new AsyncCommand(ImportAsync);
        // parameter "all" (toolbar) exports everything; a row / no parameter exports the selection
        ExportSavedCommand = new RelayCommand(p => Export(p as string == "all" ? SavedItems.ToList() : SavedTargets(p)),
            p => p as string == "all" ? SavedItems.Count > 0 : SavedTargets(p).Count > 0);
        SaveDefinitionCommand = new RelayCommand(SaveDefinition, () => SelectedSaved != null && DefinitionDirty);
        RevertDefinitionCommand = new RelayCommand(() => { DefinitionText = SelectedSaved?.Json ?? ""; }, () => DefinitionDirty);
        OpenSavedFileCommand = new RelayCommand(() => OpenPathCommand.Execute(AppLibrary.FilePath));
        RebuildSaved();
    }

    // ---------------- view switching ----------------

    private string _viewMode = "processes";
    public string ViewMode
    {
        get => _viewMode;
        set
        {
            if (!Set(ref _viewMode, value)) return;
            Raise(nameof(IsSavedView)); Raise(nameof(IsProcessView));
            Raise(nameof(ShowList)); Raise(nameof(ShowNotRunning)); Raise(nameof(ShowAccessDenied)); Raise(nameof(ShowNoPm2));
            Raise(nameof(ShowError)); Raise(nameof(ShowConnecting)); Raise(nameof(IsEmpty)); Raise(nameof(HasSelection));
            Raise(nameof(ShowProcessDetail)); Raise(nameof(ShowSavedDetail)); Raise(nameof(ShowNoSelection)); Raise(nameof(HasMultiSelection));
            if (value == "saved") RefreshSavedStates();
        }
    }
    public bool IsSavedView => _viewMode == "saved";
    public bool IsProcessView => _viewMode == "processes";
    public bool ShowProcessDetail => IsProcessView && _selected != null;
    public bool ShowSavedDetail => IsSavedView && _selectedSaved != null;
    public bool ShowNoSelection => !ShowProcessDetail && !ShowSavedDetail;
    public bool SavedEmpty => SavedItems.Count == 0;
    public int SavedCount => SavedItems.Count;
    public int MissingCount => SavedItems.Count(s => s.IsMissing);

    public bool AutoSyncSaved
    {
        get => Settings.AutoSyncSavedList;
        set { if (Settings.AutoSyncSavedList == value) return; Settings.AutoSyncSavedList = value; Settings.Save(); Raise(); if (value) SyncLibrary(false); }
    }

    // ---------------- selection + editing ----------------

    private SavedApp? _selectedSaved;
    public SavedApp? SelectedSaved
    {
        get => _selectedSaved;
        set
        {
            if (!Set(ref _selectedSaved, value)) return;
            DefinitionText = value?.Json ?? "";
            DefinitionError = "";
            Raise(nameof(ShowSavedDetail)); Raise(nameof(ShowNoSelection));
        }
    }

    private string _definitionText = "";
    public string DefinitionText
    {
        get => _definitionText;
        set { if (Set(ref _definitionText, value)) { Raise(nameof(DefinitionDirty)); CommandManager.InvalidateRequerySuggested(); } }
    }
    public bool DefinitionDirty => _selectedSaved != null && _definitionText != _selectedSaved.Json;
    private string _definitionError = "";
    public string DefinitionError { get => _definitionError; private set { if (Set(ref _definitionError, value)) Raise(nameof(HasDefinitionError)); } }
    public bool HasDefinitionError => _definitionError.Length > 0;

    private void SaveDefinition()
    {
        var s = _selectedSaved;
        if (s == null) return;
        try
        {
            var node = JsonNode.Parse(_definitionText, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (node is not JsonObject o) throw new InvalidOperationException("The definition must be a JSON object: { \"name\": …, \"script\": … }");
            var name = AppLibrary.Name(o) ?? throw new InvalidOperationException("\"name\" is required.");
            if (o["script"] == null) throw new InvalidOperationException("\"script\" is required.");
            if (!name.Equals(s.Name, StringComparison.OrdinalIgnoreCase) && _library.Contains(name))
                throw new InvalidOperationException($"Another saved app is already named '{name}'.");
            if (!name.Equals(s.Name, StringComparison.OrdinalIgnoreCase)) _library.Remove(s.Name);
            _library.Upsert(o);
            _library.Save();
            DefinitionError = "";
            RebuildSaved(name);
            App.Toast("Saved", name, ToastKind.Success);
        }
        catch (JsonException ex) { DefinitionError = "Invalid JSON: " + ex.Message; }
        catch (Exception ex) { DefinitionError = ex.Message; }
    }

    private List<SavedApp> SavedTargets(object? p)
    {
        if (p is SavedApp one) return new List<SavedApp> { one };
        var sel = SavedItems.Where(i => i.IsSelected).ToList();
        if (sel.Count > 0) return sel;
        return _selectedSaved != null ? new List<SavedApp> { _selectedSaved } : new List<SavedApp>();
    }

    // ---------------- sync with pm2 ----------------

    /// <summary>
    /// Copies every pm2 app into the Saved list (added or changed ones only). Never removes anything:
    /// an app that disappears from pm2 stays saved and shows "not in pm2", ready to be started again.
    /// </summary>
    private int SyncLibrary(bool force)
    {
        if (!IsConnected || (!force && !Settings.AutoSyncSavedList)) { RefreshSavedStates(); return 0; }
        int changed = 0;
        foreach (var byName in Items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            var def = byName.First().P.Definition;
            if (def != null && _library.Upsert(def)) changed++;
        }
        if (changed > 0)
        {
            try { _library.Save(); } catch (Exception ex) { App.Toast("Could not save the list", ex.Message, ToastKind.Error); }
            RebuildSaved(_selectedSaved?.Name);
        }
        else RefreshSavedStates();
        return changed;
    }

    private void RebuildSaved(string? select = null)
    {
        var keepSel = select ?? _selectedSaved?.Name;
        var byName = SavedItems.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        var wanted = _library.Apps.ToList();
        var names = new HashSet<string>(wanted.Select(w => w.Name), StringComparer.OrdinalIgnoreCase);
        for (int i = SavedItems.Count - 1; i >= 0; i--) if (!names.Contains(SavedItems[i].Name)) SavedItems.RemoveAt(i);
        int idx = 0;
        foreach (var (name, def, updated) in wanted)
        {
            if (byName.TryGetValue(name, out var existing))
            {
                existing.SetDef(def, updated);
                int cur = SavedItems.IndexOf(existing);
                if (cur != idx) SavedItems.Move(cur, idx);
            }
            else SavedItems.Insert(idx, new SavedApp(name, def, updated));
            idx++;
        }
        var sel = keepSel == null ? null : SavedItems.FirstOrDefault(s => s.Name.Equals(keepSel, StringComparison.OrdinalIgnoreCase));
        if (sel != _selectedSaved) SelectedSaved = sel;
        else if (sel != null && !DefinitionDirty) { _definitionText = sel.Json; Raise(nameof(DefinitionText)); Raise(nameof(DefinitionDirty)); }
        RefreshSavedStates();
        Raise(nameof(SavedEmpty)); Raise(nameof(SavedCount));
    }

    private void RefreshSavedStates()
    {
        var live = Items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Any(i => i.IsOnline) ? "online" : g.Any(i => i.IsErrored) ? "errored" : "stopped", StringComparer.OrdinalIgnoreCase);
        foreach (var s in SavedItems)
            s.Pm2State = !IsConnected ? (State == ConnState.NotRunning ? "missing" : "unknown")
                : live.TryGetValue(s.Name, out var st) ? st : "missing";
        Raise(nameof(MissingCount));
        CommandManager.InvalidateRequerySuggested();
    }

    // ---------------- start / remove ----------------

    private async Task StartSavedAsync(List<SavedApp> apps)
    {
        if (apps.Count == 0) return;
        Directory.CreateDirectory(Path.Combine(Settings.Dir, "apps"));
        var file = Path.Combine(Settings.Dir, "apps", $"_saved-start-{DateTime.Now:yyyyMMddHHmmss}.json");
        await File.WriteAllTextAsync(file, AppLibrary.Ecosystem(apps.Select(a => a.Def)), new UTF8Encoding(false));
        try
        {
            var names = apps.Count == 1 ? apps[0].Name : $"{apps.Count} apps";
            bool ok = await CliAsync("Started from the Saved list", names, "start", file);
            if (ok && Settings.SaveAfterStartingSaved) await Pm2Cli.Pm2Async(new[] { "save" });
        }
        finally { try { File.Delete(file); } catch { } }
        RefreshSavedStates();
    }

    private async Task RemoveSavedAsync(List<SavedApp> apps)
    {
        if (apps.Count == 0) return;
        var what = apps.Count == 1 ? $"'{apps[0].Name}'" : $"{apps.Count} apps";
        bool inPm2 = apps.Any(a => !a.IsMissing && a.Pm2State != "unknown");
        if (Settings.ConfirmDestructive && !await ConfirmAsync($"Remove {what} from the Saved list?",
                "Only ampm2's saved copy is removed." + (inPm2 ? "\nThe process keeps running in pm2" + (Settings.AutoSyncSavedList ? ", and automatic sync will save it again. Delete it from pm2 first, or turn automatic sync off." : ".") : ""),
                "Remove", true))
            return;
        foreach (var a in apps) _library.Remove(a.Name);
        _library.Save();
        RebuildSaved();
    }

    // ---------------- import / export ----------------

    private async Task ImportAsync()
    {
        var d = new OpenFileDialog
        {
            Title = "Import apps into the Saved list",
            Filter = "pm2 ecosystem or dump|*.json;*.config.js;*.config.cjs;*.cjs;*.js;*.pm2|All files|*.*",
            InitialDirectory = Directory.Exists(Pm2Home) ? Pm2Home : null,
        };
        if (d.ShowDialog(Application.Current.MainWindow) != true) return;
        List<JsonObject> apps;
        try { apps = await Task.Run(() => AppLibrary.ReadFile(d.FileName)); }
        catch (Exception ex) { App.Toast("Import failed", ex.Message, ToastKind.Error); return; }
        if (apps.Count == 0) { App.Toast("Nothing imported", "The file contains no apps.", ToastKind.Info); return; }

        var conflicts = apps.Where(a => _library.Contains(AppLibrary.Name(a)!)).Select(a => AppLibrary.Name(a)!).ToList();
        if (conflicts.Count > 0 && !await ConfirmAsync($"Replace {conflicts.Count} saved app{(conflicts.Count == 1 ? "" : "s")}?",
                $"These names are already in the Saved list: {string.Join(", ", conflicts.Take(8))}{(conflicts.Count > 8 ? "…" : "")}.\nImporting replaces their saved definitions.", "Replace"))
            apps = apps.Where(a => !conflicts.Contains(AppLibrary.Name(a)!)).ToList();

        int added = 0, updated = 0;
        foreach (var a in apps)
        {
            bool existed = _library.Contains(AppLibrary.Name(a)!);
            if (_library.Upsert(a)) { if (existed) updated++; else added++; }
        }
        _library.Save();
        RebuildSaved();
        ViewMode = "saved";
        App.Toast("Imported", $"{added} added, {updated} updated from {Path.GetFileName(d.FileName)}.", ToastKind.Success);
    }

    private void Export(List<SavedApp> all)
    {
        if (all.Count == 0) return;
        var d = new SaveFileDialog
        {
            Title = "Export as a pm2 ecosystem file",
            FileName = all.Count == 1 ? $"{all[0].Name}.ecosystem.json" : "ecosystem.json",
            Filter = "pm2 ecosystem (JSON)|*.json",
        };
        if (d.ShowDialog(Application.Current.MainWindow) != true) return;
        try
        {
            File.WriteAllText(d.FileName, AppLibrary.Ecosystem(all.Select(a => a.Def)), new UTF8Encoding(false));
            App.Toast("Exported", $"{all.Count} app{(all.Count == 1 ? "" : "s")}. Start them anywhere with: pm2 start {Path.GetFileName(d.FileName)}", ToastKind.Success);
        }
        catch (Exception ex) { App.Toast("Export failed", ex.Message, ToastKind.Error); }
    }

    public void UpdateSavedSelectionCount() => SavedSelectedCount = SavedItems.Count(i => i.IsSelected);
    private int _savedSelectedCount;
    public int SavedSelectedCount { get => _savedSelectedCount; private set { if (Set(ref _savedSelectedCount, value)) Raise(nameof(HasSavedMultiSelection)); } }
    public bool HasSavedMultiSelection => _savedSelectedCount > 1;
}
