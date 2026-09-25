using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ampm2.Pm2;
using Ampm2.Sys;
using Avalonia.Threading;

namespace Ampm2.Mac.ViewModels;

public enum ConnState { Connecting, Connected, NotRunning, AccessDenied, NoPm2, Error }

/// <summary>Everything the Mac window shows. Same behaviour as the Windows app, on the shared core.</summary>
public sealed class MainViewModel : ObservableObject
{
    public MacSettings Settings { get; }
    private readonly List<ProcessRow> _all = new();
    public ObservableCollection<ProcessRow> Items { get; } = new();          // filtered + sorted view
    public ObservableCollection<SavedRow> SavedItems { get; } = new();
    public ObservableCollection<LogLine> Logs { get; } = new();
    public ObservableCollection<Toast> Toasts { get; } = new();

    private Pm2Rpc? _rpc;
    private Pm2Bus? _bus;
    private readonly IProcessMetrics _metrics = ProcessMetricsFactory.Create();
    private readonly DispatcherTimer _metricsTimer, _listTimer, _retryTimer, _eventDebounce, _logFlush;
    private readonly ConcurrentQueue<LogEvent> _pendingLogs = new();
    private AppLibrary _library = AppLibrary.Load();
    private bool _connecting, _visible = true, _refreshing, _refreshAgain;

    public MainViewModel(MacSettings settings)
    {
        Settings = settings;
        _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(settings.MetricsIntervalSec, 1, 30)) };
        _metricsTimer.Tick += (_, _) => Guard(SampleMetrics);
        _listTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(settings.ListIntervalSec, 10, 3600)) };
        _listTimer.Tick += async (_, _) => await GuardAsync(RefreshListAsync);
        _retryTimer = new DispatcherTimer();
        _retryTimer.Tick += async (_, _) => { _retryTimer.Stop(); await GuardAsync(ConnectAsync); };
        _eventDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _eventDebounce.Tick += async (_, _) => { _eventDebounce.Stop(); await GuardAsync(RefreshListAsync); };
        _logFlush = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _logFlush.Tick += (_, _) => Guard(FlushLogs);

        RefreshCommand = Command.Async(async () => { if (IsConnected) await RefreshListAsync(); else await ConnectAsync(); });
        StartCommand = new Command(p => ActAsync(p, "start"), _ => IsConnected);
        ToggleRunCommand = new Command(p => ActAsync(p, ((p as ProcessRow) ?? _selected)?.CanStop == true ? "stop" : "start"), _ => IsConnected);
        StartAllCommand = Command.Async(() => ActOnAsync(_all.Where(i => i.CanStart).ToList(), "start"), () => IsConnected && _all.Any(i => i.CanStart));
        StopCommand = new Command(p => ActAsync(p, "stop"), _ => IsConnected);
        RestartCommand = new Command(p => ActAsync(p, "restart"), _ => IsConnected);
        ReloadCommand = new Command(p => ActAsync(p, "reload"), _ => IsConnected);
        DeleteCommand = new Command(p => ActAsync(p, "delete"), _ => IsConnected);
        RestartAllCommand = Command.Async(() => AllAsync("restart"), () => IsConnected && _all.Count > 0);
        StopAllCommand = Command.Async(() => AllAsync("stop"), () => IsConnected && _all.Any(i => i.CanStop));
        SaveCommand = Command.Async(() => CliAsync("Saved", "Process list saved to dump.pm2.", "save"), () => IsConnected);
        ResurrectCommand = Command.Async(() => CliAsync("Resurrected", "Saved processes restored.", "resurrect"), () => CanUseCli);
        StartDaemonCommand = Command.Async(StartDaemonAsync, () => State == ConnState.NotRunning && Pm2Endpoints.IsDefault);
        FlushCommand = new Command(p => FlushAsync(p as ProcessRow), _ => IsConnected);
        InstallNodeCommand = Command.Async(InstallNodeAsync, () => !Installing);
        InstallPm2Command = Command.Async(InstallPm2Async, () => !Installing && Pm2Cli.NodePath != null);
        OpenFolderCommand = new Command(p => Reveal(p as string));
        ShowLogsCommand = new Command(p => { if (p is ProcessRow r) Selected = r; DetailTab = "logs"; });
        ClearLogsCommand = new Command(() => Logs.Clear());
        ReloadLogsCommand = new Command(LoadLogHistory);
        SetFilterCommand = new Command(p => { StatusFilter = p as string ?? "all"; RaiseSegments(); });
        SortCommand = new Command(p => SetSort(p as string ?? "id"));
        SetViewCommand = new Command(p => { ViewMode = p as string ?? "processes"; RaiseSegments(); });
        SetTabCommand = new Command(p => { DetailTab = p as string ?? "overview"; RaiseSegments(); });
        SetStreamCommand = new Command(p => { LogStream = p as string ?? "all"; RaiseSegments(); });
        OpenSettingsCommand = new Command(() => { AboutOpen = false; SettingsOpen = true; });
        CloseSettingsCommand = new Command(() => { SettingsOpen = false; Settings.Save(); ApplyIntervals(); });
        OpenAboutCommand = new Command(() => { SettingsOpen = false; AboutOpen = true; _ = Update.CheckAsync(quiet: true); });
        CheckUpdateCommand = Command.Async(() => Update.CheckAsync());
        InstallUpdateCommand = Command.Async(() => Update.InstallAsync(App.Quit));
        OpenReleaseCommand = new Command(() => OpenUrl(Update.ReleasePage));
        CloseAboutCommand = new Command(() => AboutOpen = false);
        OpenEmailCommand = new Command(() => OpenUrl("mailto:" + AppInfo.Email + "?subject=ampm2%20" + AppInfo.Version));
        OpenGitHubCommand = new Command(() => OpenUrl(AppInfo.GitHubUrl), () => HasGitHub);
        OpenLicenseCommand = new Command(() => OpenUrl(AppInfo.LicenseUrl));
        OpenWebsiteCommand = new Command(() => OpenUrl(AppInfo.Website));
        OpenHelpCommand = new Command(p => App.ShowHelp(p as string));
        CopyAboutCommand = Command.Async(() => App.CopyText($"{AppInfo.Name} {AppInfo.Version} ({AppInfo.Platform}, {AppInfo.Runtime})\npm2 {(Pm2Version.Length > 0 ? Pm2Version : "not connected")}\n{AppInfo.Author} <{AppInfo.Email}>", "About details copied."));
        DialogOkCommand = new Command(() => CloseDialog(true));
        DialogCancelCommand = new Command(() => CloseDialog(false));
        AddCommand = Command.Async(() => App.ShowAddWindowAsync(), () => CanUseCli);

        SyncSavedNowCommand = new Command(() =>
        {
            int n = SyncLibrary(true);
            App.Toast("Saved list updated", n == 0 ? "Already up to date." : $"{n} app{(n == 1 ? "" : "s")} saved from pm2.", ToastKind.Success);
        }, () => IsConnected && _all.Count > 0);
        StartSavedCommand = new Command(p => StartSavedAsync(p is SavedRow s ? new List<SavedRow> { s } : SelectedSaved != null ? new List<SavedRow> { SelectedSaved } : new()),
            p => CanUseCli && ((p as SavedRow) ?? SelectedSaved)?.IsMissing == true);
        StartMissingCommand = Command.Async(() => StartSavedAsync(SavedItems.Where(s => s.IsMissing).ToList()), () => CanUseCli && SavedItems.Any(s => s.IsMissing));
        RemoveSavedCommand = new Command(p => RemoveSavedAsync((p as SavedRow) ?? SelectedSaved), p => ((p as SavedRow) ?? SelectedSaved) != null);
        ImportSavedCommand = Command.Async(ImportAsync);
        ExportSavedCommand = new Command(p => ExportAsync(p as string == "all" ? SavedItems.ToList() : ((p as SavedRow) ?? SelectedSaved) is { } one ? new List<SavedRow> { one } : new()),
            p => p as string == "all" ? SavedItems.Count > 0 : ((p as SavedRow) ?? SelectedSaved) != null);
        SaveDefinitionCommand = new Command(SaveDefinition, () => SelectedSaved != null && DefinitionDirty);
        RevertDefinitionCommand = new Command(() => DefinitionText = SelectedSaved?.Json ?? "", () => DefinitionDirty);
        RevealSavedFileCommand = new Command(() => Reveal(AppLibrary.FilePath));

        AppLibrary.Warning += (t, m) => App.Toast(t, m, ToastKind.Error);
        RebuildSaved();
    }

    // ================= connection =================

    private ConnState _state = ConnState.Connecting;
    public ConnState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value)) return;
            foreach (var n in new[] { nameof(IsConnected), nameof(StateText), nameof(ShowList), nameof(ShowNotRunning), nameof(ShowAccessDenied),
                                      nameof(ShowNoPm2), nameof(ShowError), nameof(ShowConnecting), nameof(IsEmpty), nameof(CanUseCli) })
                Raise(n);
            RefreshSavedStates();
            Command.Requery();
        }
    }
    public bool IsConnected => State == ConnState.Connected;
    public bool ShowList => IsProcessView && IsConnected;
    public bool ShowNotRunning => IsProcessView && State == ConnState.NotRunning;
    public bool ShowAccessDenied => IsProcessView && State == ConnState.AccessDenied;
    public bool ShowNoPm2 => IsProcessView && State == ConnState.NoPm2;
    public bool ShowError => IsProcessView && State == ConnState.Error;
    public bool ShowConnecting => IsProcessView && State == ConnState.Connecting;
    public bool IsEmpty => IsProcessView && IsConnected && _all.Count == 0;
    /// <summary>The CLI must reach the same daemon the window shows (tests override the socket, then AMPM2_PM2 must be set too).</summary>
    public bool CanUseCli => State is ConnState.Connected or ConnState.NotRunning &&
                             (Pm2Endpoints.IsDefault || Environment.GetEnvironmentVariable("AMPM2_PM2") is { Length: > 0 });

    private string _errorText = "";
    public string ErrorText { get => _errorText; private set => Set(ref _errorText, value); }
    private string _version = "";
    public string Pm2Version { get => _version; private set { if (Set(ref _version, value)) Raise(nameof(StateText)); } }
    private int _daemonPid;
    public string StateText => State switch
    {
        ConnState.Connected => $"pm2 v{Pm2Version}" + (_daemonPid > 0 ? $" · daemon pid {_daemonPid}" : ""),
        ConnState.Connecting => "Connecting…",
        ConnState.NotRunning => "Daemon not running",
        ConnState.AccessDenied => "No access to the daemon",
        ConnState.NoPm2 => "pm2 not installed",
        _ => "Connection error",
    };

    public async Task ConnectAsync()
    {
        if (_connecting || IsConnected) return;
        _connecting = true;
        _retryTimer.Stop();
        try
        {
            var endpoint = Pm2Endpoints.Rpc;
            if (!PipeConnection.PipeExists(endpoint))
            {
                State = await Task.Run(() => Pm2Cli.Pm2Path) == null ? ConnState.NoPm2 : ConnState.NotRunning;
                Retry(5);
                return;
            }
            Pm2Rpc rpc;
            try { rpc = await Pm2Rpc.ConnectAsync(endpoint, CancellationToken.None); }
            catch (PipeConnectException ex)
            {
                State = ex.Kind switch { ConnectFailure.AccessDenied => ConnState.AccessDenied, ConnectFailure.NotRunning => ConnState.NotRunning, _ => ConnState.Error };
                ErrorText = ex.Message;
                Retry(ex.Kind == ConnectFailure.AccessDenied ? 15 : 5);
                return;
            }
            _rpc = rpc;
            rpc.Disconnected += _ => Dispatcher.UIThread.Post(() => OnDisconnected(rpc));
            try
            {
                var bus = await Pm2Bus.ConnectAsync(Pm2Endpoints.Pub, CancellationToken.None);
                bus.ProcessEvent += (_, _) => Dispatcher.UIThread.Post(() => { _eventDebounce.Stop(); _eventDebounce.Start(); });
                bus.Log += e => _pendingLogs.Enqueue(e);
                bus.LogFilter = _logTarget;
                _bus = bus;
            }
            catch { /* live events are optional */ }
            _daemonPid = rpc.DaemonPid;
            Pm2Version = await rpc.GetVersionAsync();
            State = ConnState.Connected;
            Raise(nameof(StateText));
            await RefreshListAsync();
            UpdateTimers();
        }
        catch (Exception ex) { ErrorText = ex.Message; State = ConnState.Error; Retry(5); }
        finally { _connecting = false; }
    }

    private static void Guard(Action a)
    {
        try { a(); } catch (Exception ex) { App.ReportError(ex); }
    }

    private static async Task GuardAsync(Func<Task> a)
    {
        try { await a(); } catch (Exception ex) { App.ReportError(ex); }
    }

    private void Retry(int s) { _retryTimer.Interval = TimeSpan.FromSeconds(s); _retryTimer.Start(); }

    private void OnDisconnected(Pm2Rpc rpc)
    {
        if (_rpc != rpc) return;
        _rpc = null;
        try { _bus?.Dispose(); } catch { }
        _bus = null;
        try { rpc.Dispose(); } catch { }
        State = ConnState.Connecting;
        foreach (var i in _all) i.ClearMetrics();
        UpdateTimers();
        Retry(2);
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        UpdateTimers();
        if (visible && IsConnected) { _ = RefreshListAsync(); SampleMetrics(); }
    }

    private void UpdateTimers()
    {
        bool on = _visible && IsConnected;
        if (on) { _metricsTimer.Start(); _listTimer.Start(); } else { _metricsTimer.Stop(); _listTimer.Stop(); }
        if (on && LogsVisible) _logFlush.Start(); else _logFlush.Stop();
    }

    public void ApplyIntervals()
    {
        _metricsTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(Settings.MetricsIntervalSec, 1, 30));
        _listTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(Settings.ListIntervalSec, 10, 3600));
    }

    // ================= list =================

    public async Task RefreshListAsync()
    {
        var rpc = _rpc;
        if (rpc == null) return;
        if (_refreshing) { _refreshAgain = true; return; }
        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                List<Pm2Process> list;
                try { list = await rpc.GetProcessesAsync(); }
                catch (Exception ex) when (ex is not Pm2Exception) { return; }
                Merge(list);
            } while (_refreshAgain);
        }
        finally { _refreshing = false; }
    }

    private void Merge(List<Pm2Process> list)
    {
        var byId = _all.ToDictionary(i => i.Id);
        var seen = new HashSet<int>();
        foreach (var p in list)
        {
            seen.Add(p.Id);
            if (byId.TryGetValue(p.Id, out var row)) row.Update(p); else _all.Add(new ProcessRow(p));
        }
        _all.RemoveAll(r => !seen.Contains(r.Id));
        if (Selected != null && !seen.Contains(Selected.Id)) Selected = null;
        ApplyView();
        if (Selected == null && _autoSelect && Items.Count > 0) { _autoSelect = false; Selected = Items[0]; }
        SampleMetrics();
        OnlineCount = _all.Count(i => i.StatusKey == "Online");
        StoppedCount = _all.Count(i => i.StatusKey == "Stopped");
        ErroredCount = _all.Count(i => i.StatusKey == "Errored");
        Raise(nameof(TotalCount)); Raise(nameof(IsEmpty));
        SyncLibrary(false);
        App.UpdateTrayTooltip();
        Command.Requery();
    }
    private bool _autoSelect = true;

    /// <summary>Rebuilds the visible collection from the filter/sort, moving rows instead of recreating them.</summary>
    private void ApplyView()
    {
        IEnumerable<ProcessRow> q = _all;
        if (_statusFilter != "all") q = q.Where(i => string.Equals(i.StatusKey, _statusFilter, StringComparison.OrdinalIgnoreCase));
        if (_search.Length > 0)
            q = q.Where(i => i.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) || i.IdText == _search || i.Script.Contains(_search, StringComparison.OrdinalIgnoreCase));
        Func<ProcessRow, IComparable> key = _sortKey switch
        {
            "name" => i => i.Name.ToLowerInvariant(),
            "status" => i => i.StatusKey,
            "cpu" => i => i.Cpu,
            "mem" => i => i.Memory,
            "restarts" => i => i.Restarts,
            "uptime" => i => i.IsOnline ? -i.P.UptimeSince : long.MinValue,
            _ => i => i.Id,
        };
        var wanted = (_sortDesc ? q.OrderByDescending(key).ThenBy(i => i.Id) : q.OrderBy(key).ThenBy(i => i.Id)).ToList();
        for (int i = Items.Count - 1; i >= 0; i--) if (!wanted.Contains(Items[i])) Items.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            int cur = Items.IndexOf(wanted[i]);
            if (cur < 0) Items.Insert(i, wanted[i]);
            else if (cur != i) Items.Move(cur, i);
        }
    }

    private void SampleMetrics()
    {
        if (_all.Count == 0) return;
        var roots = _all.Where(i => i.IsOnline && i.P.Pid > 0).Select(i => i.P.Pid).ToList();
        var usage = _metrics.Sample(roots);
        double c = 0; long m = 0;
        foreach (var i in _all)
        {
            if (i.IsOnline && usage.TryGetValue(i.P.Pid, out var u)) { i.ApplyMetrics(u.CpuPercent, u.MemoryBytes, u.ProcessCount); c += u.CpuPercent; m += u.MemoryBytes; }
            else if (!i.IsOnline) i.ClearMetrics();
            i.Tick();
        }
        long maxMem = 1;
        foreach (var i in _all) if (i.Memory > maxMem) maxMem = i.Memory;
        foreach (var i in _all) i.SetMemShare((double)i.Memory / maxMem);
        Selected?.BuildSparklines(360, 56);
        if (_sortKey is "cpu" or "mem") ApplyView();
        TotalCpuText = $"{c:0.#}%";
        TotalMemText = Fmt.Bytes(m);
    }

    private int _online, _stopped, _errored;
    public int OnlineCount { get => _online; private set => Set(ref _online, value); }
    public int StoppedCount { get => _stopped; private set => Set(ref _stopped, value); }
    public int ErroredCount { get => _errored; private set => Set(ref _errored, value); }
    public int TotalCount => _all.Count;
    private string _totalCpu = "—", _totalMem = "—";
    public string TotalCpuText { get => _totalCpu; private set => Set(ref _totalCpu, value); }
    public string TotalMemText { get => _totalMem; private set => Set(ref _totalMem, value); }

    // ================= filter / sort / view =================

    private string _search = "";
    public string SearchText { get => _search; set { if (Set(ref _search, value ?? "")) ApplyView(); } }
    private string _statusFilter = "all";
    public string StatusFilter { get => _statusFilter; set { if (Set(ref _statusFilter, value)) ApplyView(); } }
    private string _sortKey = "id";
    private bool _sortDesc;
    private void SetSort(string key)
    {
        if (_sortKey == key) _sortDesc = !_sortDesc; else { _sortKey = key; _sortDesc = key is "cpu" or "mem" or "restarts" or "uptime"; }
        ApplyView();
    }

    private string _viewMode = "processes";
    public string ViewMode
    {
        get => _viewMode;
        set
        {
            if (!Set(ref _viewMode, value)) return;
            foreach (var n in new[] { nameof(IsSavedView), nameof(IsProcessView), nameof(ShowList), nameof(ShowNotRunning), nameof(ShowAccessDenied),
                                      nameof(ShowNoPm2), nameof(ShowError), nameof(ShowConnecting), nameof(IsEmpty), nameof(ShowProcessDetail),
                                      nameof(ShowSavedDetail), nameof(ShowNoSelection), nameof(SavedEmptyVisible) })
                Raise(n);
            if (value == "saved") RefreshSavedStates();
        }
    }
    public bool IsSavedView => _viewMode == "saved";
    public bool IsFilterAll => _statusFilter == "all";
    public bool IsFilterOnline => _statusFilter == "online";
    public bool IsFilterStopped => _statusFilter == "stopped";
    public bool IsFilterErrored => _statusFilter == "errored";
    public bool IsStreamAll => _logStream == "all";
    public bool IsStreamOut => _logStream == "out";
    public bool IsStreamErr => _logStream == "err";

    /// <summary>
    /// Segment buttons are ToggleButtons bound one-way: clicking the active one would un-check it,
    /// so every segment command re-announces all segment states afterwards.
    /// </summary>
    private void RaiseSegments()
    {
        foreach (var n in new[] { nameof(IsProcessView), nameof(IsSavedView), nameof(IsFilterAll), nameof(IsFilterOnline), nameof(IsFilterStopped),
                                  nameof(IsFilterErrored), nameof(IsStreamAll), nameof(IsStreamOut), nameof(IsStreamErr), nameof(LogsVisible), nameof(OverviewVisible) })
            Raise(n);
    }
    public bool IsProcessView => _viewMode == "processes";

    // ================= selection / detail =================

    private ProcessRow? _selected;
    public ProcessRow? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            Raise(nameof(ShowProcessDetail)); Raise(nameof(ShowNoSelection));
            value?.BuildSparklines(360, 56);
            if (LogsVisible) SwitchLogTarget();
            Command.Requery();
        }
    }
    public bool ShowProcessDetail => IsProcessView && _selected != null;
    public bool ShowSavedDetail => IsSavedView && _selectedSaved != null;
    public bool ShowNoSelection => !ShowProcessDetail && !ShowSavedDetail;

    private string _detailTab = "overview";
    public string DetailTab
    {
        get => _detailTab;
        set
        {
            if (!Set(ref _detailTab, value)) return;
            Raise(nameof(LogsVisible)); Raise(nameof(OverviewVisible));
            if (LogsVisible) SwitchLogTarget(); else { SetLogTarget(-1); Logs.Clear(); }
            UpdateTimers();
        }
    }
    public bool LogsVisible => _detailTab == "logs";
    public bool OverviewVisible => _detailTab != "logs";

    // ================= logs =================

    private int _logTarget = -1;
    private void SetLogTarget(int id)
    {
        _logTarget = id;
        if (_bus != null) _bus.LogFilter = id;
        while (_pendingLogs.TryDequeue(out _)) { }
    }
    private void SwitchLogTarget() { SetLogTarget(_selected?.Id ?? -1); LoadLogHistory(); }

    private bool _pauseLogs;
    public bool PauseLogs { get => _pauseLogs; set => Set(ref _pauseLogs, value); }
    private string _logStream = "all";
    public string LogStream { get => _logStream; set { if (Set(ref _logStream, value)) LoadLogHistory(); } }
    public event Action<bool>? LogsAppended;

    private void LoadLogHistory()
    {
        Logs.Clear();
        var s = _selected;
        if (s == null) return;
        if (_logStream != "err")
        {
            Logs.Add(new LogLine("stdout · " + s.P.OutLog, LogKind.Marker));
            foreach (var l in LogFiles.Tail(s.P.OutLog, 300)) Logs.Add(new LogLine(l, LogKind.Out));
        }
        if (_logStream != "out")
        {
            Logs.Add(new LogLine("stderr · " + s.P.ErrLog, LogKind.Marker));
            foreach (var l in LogFiles.Tail(s.P.ErrLog, 300)) Logs.Add(new LogLine(l, LogKind.Err));
        }
        Logs.Add(new LogLine(_bus != null ? "live" : "live stream unavailable, use reload", LogKind.Marker));
        LogsAppended?.Invoke(true);
    }

    private void FlushLogs()
    {
        if (_pendingLogs.IsEmpty) return;
        if (_pauseLogs) { while (_pendingLogs.Count > 5000 && _pendingLogs.TryDequeue(out _)) { } return; }
        int added = 0;
        while (added < 2000 && _pendingLogs.TryDequeue(out var e))
        {
            if (e.ProcessId != _logTarget) continue;
            if (e.IsError ? _logStream == "out" : _logStream == "err") continue;
            foreach (var raw in e.Text.Split('\n'))
            {
                var t = Pm2Cli.StripAnsi(raw.TrimEnd('\r'));
                if (t.Length == 0) continue;
                Logs.Add(new LogLine(t, e.IsError ? LogKind.Err : LogKind.Out));
                added++;
            }
        }
        if (Logs.Count > 4500) while (Logs.Count > 4000) Logs.RemoveAt(0);
        if (added > 0) LogsAppended?.Invoke(false);
    }

    // ================= actions =================

    public Command RefreshCommand { get; }
    public Command StartCommand { get; }
    public Command ToggleRunCommand { get; }
    public Command StartAllCommand { get; }
    public Command StopCommand { get; }
    public Command RestartCommand { get; }
    public Command ReloadCommand { get; }
    public Command DeleteCommand { get; }
    public Command RestartAllCommand { get; }
    public Command StopAllCommand { get; }
    public Command SaveCommand { get; }
    public Command ResurrectCommand { get; }
    public Command StartDaemonCommand { get; }
    public Command FlushCommand { get; }
    public Command InstallNodeCommand { get; }
    public Command InstallPm2Command { get; }
    public Command OpenFolderCommand { get; }
    public Command ShowLogsCommand { get; }
    public Command ClearLogsCommand { get; }
    public Command ReloadLogsCommand { get; }
    public Command SetFilterCommand { get; }
    public Command SortCommand { get; }
    public Command SetViewCommand { get; }
    public Command SetTabCommand { get; }
    public Command SetStreamCommand { get; }
    public Command OpenSettingsCommand { get; }
    public Command CloseSettingsCommand { get; }
    public Command OpenAboutCommand { get; }
    public Command CloseAboutCommand { get; }
    public Command OpenEmailCommand { get; }
    public Command OpenGitHubCommand { get; }
    public Command OpenLicenseCommand { get; }
    public Command OpenWebsiteCommand { get; }
    public Command OpenHelpCommand { get; }
    public Command CopyAboutCommand { get; }
    /// <summary>About ▸ Updates: check GitHub, download, verify and install a newer release.</summary>
    public UpdateModel Update { get; } = new();
    public Command CheckUpdateCommand { get; }
    public Command InstallUpdateCommand { get; }
    public Command OpenReleaseCommand { get; }
    public Command DialogOkCommand { get; }
    public Command DialogCancelCommand { get; }
    public Command AddCommand { get; }
    public Command SyncSavedNowCommand { get; }
    public Command StartSavedCommand { get; }
    public Command StartMissingCommand { get; }
    public Command RemoveSavedCommand { get; }
    public Command ImportSavedCommand { get; }
    public Command ExportSavedCommand { get; }
    public Command SaveDefinitionCommand { get; }
    public Command RevertDefinitionCommand { get; }
    public Command RevealSavedFileCommand { get; }

    private static string Past(string verb) => verb switch
    {
        "start" => "Started", "stop" => "Stopped", "restart" => "Restarted", "reload" => "Reloaded", "delete" => "Deleted", _ => verb,
    };

    private Task ActAsync(object? p, string verb)
    {
        var t = p as ProcessRow ?? _selected;
        return t == null ? Task.CompletedTask : ActOnAsync(new List<ProcessRow> { t }, verb);
    }

    private async Task ActOnAsync(List<ProcessRow> targets, string verb)
    {
        var rpc = _rpc;
        if (rpc == null || targets.Count == 0) return;
        if (verb == "delete" && Settings.ConfirmDestructive)
        {
            var names = targets.Count == 1 ? $"'{targets[0].Name}'" : $"{targets.Count} processes";
            if (!await ConfirmAsync($"Delete {names}?", "The process is stopped and removed from pm2. Its script and log files stay on disk.\nIt stays in ampm2's Saved list, so you can start it again later.", "Delete", true))
                return;
        }
        var failures = new List<string>();
        foreach (var t in targets) t.Busy = true;
        try
        {
            await Task.WhenAll(targets.Select(async t =>
            {
                try
                {
                    switch (verb)
                    {
                        case "start": await rpc.StartAsync(t.Id); break;
                        case "stop": await rpc.StopAsync(t.Id); break;
                        case "restart": await rpc.RestartAsync(t.Id); break;
                        case "reload": await rpc.ReloadAsync(t.Id); break;
                        case "delete": await rpc.DeleteAsync(t.Id); break;
                    }
                }
                catch (Exception ex) { lock (failures) failures.Add($"{t.Name}: {ex.Message}"); }
            }));
        }
        finally { foreach (var t in targets) t.Busy = false; }
        if (failures.Count > 0) App.Toast($"{verb} failed", string.Join("\n", failures), ToastKind.Error);
        else App.Toast(Past(verb), targets.Count == 1 ? targets[0].Name : $"{targets.Count} processes", ToastKind.Success);
        await RefreshListAsync();
    }

    private async Task AllAsync(string verb)
    {
        if (Settings.ConfirmDestructive && !await ConfirmAsync(verb == "stop" ? "Stop all processes?" : "Restart all processes?",
                $"This will {verb} all {_all.Count} pm2 processes.", verb == "stop" ? "Stop all" : "Restart all", verb == "stop"))
            return;
        await ActOnAsync(verb == "stop" ? _all.Where(i => i.CanStop).ToList() : _all.ToList(), verb);
    }

    private async Task FlushAsync(ProcessRow? row)
    {
        var t = row ?? _selected;
        if (Settings.ConfirmDestructive && !await ConfirmAsync(t != null ? $"Empty the logs of '{t.Name}'?" : "Empty all pm2 logs?",
                "The log files are truncated. This cannot be undone.", "Flush", true)) return;
        if (t != null) await CliAsync("Logs flushed", t.Name, "flush", t.Id.ToString());
        else await CliAsync("Logs flushed", "All processes", "flush");
        if (LogsVisible) LoadLogHistory();
    }

    public async Task<bool> CliAsync(string okTitle, string okMessage, params string[] args)
    {
        if (!CanUseCli) { App.Toast("Not available", "ampm2 cannot reach the pm2 daemon right now.", ToastKind.Error); return false; }
        if (Pm2Cli.Pm2Path == null) { App.Toast("pm2 not found", "Install pm2 first.", ToastKind.Error); return false; }
        var r = await Pm2Cli.Pm2Async(args);
        if (r.Ok) App.Toast(okTitle, okMessage, ToastKind.Success);
        else App.Toast($"pm2 {args[0]} failed", Tail(r.Output, 6), ToastKind.Error);
        if (IsConnected) await RefreshListAsync(); else await ConnectAsync();
        return r.Ok;
    }

    private static string Tail(string s, int lines)
    {
        var l = s.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("\n", l.Skip(Math.Max(0, l.Length - lines))).Trim();
    }

    private async Task StartDaemonAsync()
    {
        var r = await Pm2Cli.Pm2Async(new[] { "ping" });
        if (!r.Ok) App.Toast("Could not start pm2", Tail(r.Output, 6), ToastKind.Error);
        await ConnectAsync();
    }

    // ================= install =================

    private bool _installing;
    public bool Installing { get => _installing; private set { if (Set(ref _installing, value)) Command.Requery(); } }
    private string _installLog = "";
    public string InstallLog { get => _installLog; private set => Set(ref _installLog, value); }
    public bool HasBrew => Pm2Cli.BrewPath != null;

    private async Task InstallNodeAsync()
    {
        var brew = Pm2Cli.BrewPath;
        if (brew == null)
        {
            App.Toast("Homebrew not found", "Opening the Node.js download page instead.", ToastKind.Info);
            OpenUrl("https://nodejs.org/en/download");
            return;
        }
        Installing = true;
        InstallLog = "brew install node …";
        try
        {
            var r = await Pm2Cli.RunAsync(brew, new[] { "install", "node" });
            InstallLog = Tail(r.Output, 8);
            App.Toast(r.Ok ? "Node.js installed" : "Node.js install failed", r.Ok ? "Now install pm2." : Tail(r.Output, 3), r.Ok ? ToastKind.Success : ToastKind.Error);
        }
        finally { Installing = false; }
    }

    private async Task InstallPm2Async()
    {
        var npm = Pm2Cli.NpmPath;
        if (npm == null) { App.Toast("npm not found", "Install Node.js first.", ToastKind.Error); return; }
        Installing = true;
        InstallLog = "npm install -g pm2 …";
        try
        {
            var r = await Pm2Cli.RunAsync(npm, new[] { "install", "-g", "pm2" });
            InstallLog = Tail(r.Output, 8);
            App.Toast(r.Ok ? "pm2 installed" : "pm2 install failed", r.Ok ? "You can start the daemon now." : Tail(r.Output, 3), r.Ok ? ToastKind.Success : ToastKind.Error);
        }
        finally { Installing = false; }
        await ConnectAsync();
    }

    // ================= Saved list =================

    public bool SavedEmptyVisible => IsSavedView && SavedItems.Count == 0;
    public int SavedCount => SavedItems.Count;
    public int MissingCount => SavedItems.Count(s => s.IsMissing);
    public bool AutoSyncSaved
    {
        get => Settings.AutoSyncSavedList;
        set { if (Settings.AutoSyncSavedList == value) return; Settings.AutoSyncSavedList = value; Settings.Save(); Raise(); if (value) SyncLibrary(false); }
    }

    private SavedRow? _selectedSaved;
    public SavedRow? SelectedSaved
    {
        get => _selectedSaved;
        set
        {
            if (!Set(ref _selectedSaved, value)) return;
            DefinitionText = value?.Json ?? "";
            DefinitionError = "";
            Raise(nameof(ShowSavedDetail)); Raise(nameof(ShowNoSelection));
            Command.Requery();
        }
    }

    private string _definitionText = "";
    public string DefinitionText
    {
        get => _definitionText;
        set { if (Set(ref _definitionText, value ?? "")) { Raise(nameof(DefinitionDirty)); Command.Requery(); } }
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
            if (JsonNode.Parse(_definitionText, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) is not JsonObject o)
                throw new InvalidOperationException("The definition must be a JSON object: { \"name\": …, \"script\": … }");
            var name = AppLibrary.Name(o) ?? throw new InvalidOperationException("\"name\" is required.");
            if (o["script"] == null) throw new InvalidOperationException("\"script\" is required.");
            if (!name.Equals(s.Name, StringComparison.OrdinalIgnoreCase) && _library.Contains(name)) throw new InvalidOperationException($"Another saved app is already named '{name}'.");
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

    /// <summary>Copies pm2's apps into the Saved list (added/changed only; never removes).</summary>
    private int SyncLibrary(bool force)
    {
        if (!IsConnected || (!force && !Settings.AutoSyncSavedList)) { RefreshSavedStates(); return 0; }
        int changed = 0;
        foreach (var g in _all.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            if (g.First().P.Definition is { } def && _library.Upsert(def)) changed++;
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
        var keep = select ?? _selectedSaved?.Name;
        var byName = SavedItems.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        var wanted = _library.Apps.ToList();
        var names = new HashSet<string>(wanted.Select(w => w.Name), StringComparer.OrdinalIgnoreCase);
        for (int i = SavedItems.Count - 1; i >= 0; i--) if (!names.Contains(SavedItems[i].Name)) SavedItems.RemoveAt(i);
        int idx = 0;
        foreach (var (name, def, updated) in wanted)
        {
            if (byName.TryGetValue(name, out var row))
            {
                row.SetDef(def, updated);
                int cur = SavedItems.IndexOf(row);
                if (cur != idx) SavedItems.Move(cur, idx);
            }
            else SavedItems.Insert(idx, new SavedRow(name, def, updated));
            idx++;
        }
        var sel = keep == null ? null : SavedItems.FirstOrDefault(s => s.Name.Equals(keep, StringComparison.OrdinalIgnoreCase));
        if (sel != _selectedSaved) SelectedSaved = sel;
        else if (sel != null && !DefinitionDirty) { _definitionText = sel.Json; Raise(nameof(DefinitionText)); Raise(nameof(DefinitionDirty)); }
        RefreshSavedStates();
        Raise(nameof(SavedCount)); Raise(nameof(SavedEmptyVisible));
    }

    private void RefreshSavedStates()
    {
        var live = _all.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Any(i => i.IsOnline) ? "online" : g.Any(i => i.IsErrored) ? "errored" : "stopped", StringComparer.OrdinalIgnoreCase);
        foreach (var s in SavedItems)
            s.State = !IsConnected ? (State == ConnState.NotRunning ? "missing" : "unknown") : live.TryGetValue(s.Name, out var st) ? st : "missing";
        Raise(nameof(MissingCount));
        Command.Requery();
    }

    private async Task StartSavedAsync(List<SavedRow> apps)
    {
        apps = apps.Where(a => a.IsMissing).ToList();
        if (apps.Count == 0) return;
        Directory.CreateDirectory(Path.Combine(DataPaths.Dir, "apps"));
        var file = Path.Combine(DataPaths.Dir, "apps", $"_saved-start-{DateTime.Now:yyyyMMddHHmmss}.json");
        await File.WriteAllTextAsync(file, AppLibrary.Ecosystem(apps.Select(a => a.Def)), new UTF8Encoding(false));
        try
        {
            bool ok = await CliAsync("Started from the Saved list", apps.Count == 1 ? apps[0].Name : $"{apps.Count} apps", "start", file);
            if (ok && Settings.SaveAfterStartingSaved) await Pm2Cli.Pm2Async(new[] { "save" });
        }
        finally { try { File.Delete(file); } catch { } }
        RefreshSavedStates();
    }

    private async Task RemoveSavedAsync(SavedRow? row)
    {
        if (row == null) return;
        bool inPm2 = !row.IsMissing && row.State != "unknown";
        if (Settings.ConfirmDestructive && !await ConfirmAsync($"Remove '{row.Name}' from the Saved list?",
                "Only ampm2's saved copy is removed." + (inPm2 ? "\nThe process keeps running in pm2" + (Settings.AutoSyncSavedList ? ", and automatic sync will save it again." : ".") : ""),
                "Remove", true))
            return;
        _library.Remove(row.Name);
        _library.Save();
        RebuildSaved();
    }

    private async Task ImportAsync()
    {
        var path = await App.PickOpenFileAsync("Import apps into the Saved list", new[] { "*.json", "*.js", "*.cjs", "*.pm2" });
        if (path == null) return;
        List<JsonObject> apps;
        try { apps = await Task.Run(() => AppLibrary.ReadFile(path)); }
        catch (Exception ex) { App.Toast("Import failed", ex.Message, ToastKind.Error); return; }
        if (apps.Count == 0) { App.Toast("Nothing imported", "The file contains no apps.", ToastKind.Info); return; }
        var conflicts = apps.Select(a => AppLibrary.Name(a)!).Where(_library.Contains).ToList();
        if (conflicts.Count > 0 && !await ConfirmAsync($"Replace {conflicts.Count} saved app{(conflicts.Count == 1 ? "" : "s")}?",
                $"Already in the Saved list: {string.Join(", ", conflicts.Take(8))}{(conflicts.Count > 8 ? "…" : "")}.", "Replace"))
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
        App.Toast("Imported", $"{added} added, {updated} updated from {Path.GetFileName(path)}.", ToastKind.Success);
    }

    private async Task ExportAsync(List<SavedRow> rows)
    {
        if (rows.Count == 0) return;
        var path = await App.PickSaveFileAsync("Export as a pm2 ecosystem file", rows.Count == 1 ? $"{rows[0].Name}.ecosystem.json" : "ecosystem.json");
        if (path == null) return;
        try
        {
            await File.WriteAllTextAsync(path, AppLibrary.Ecosystem(rows.Select(r => r.Def)), new UTF8Encoding(false));
            App.Toast("Exported", $"{rows.Count} app{(rows.Count == 1 ? "" : "s")}. Start them anywhere with: pm2 start {Path.GetFileName(path)}", ToastKind.Success);
        }
        catch (Exception ex) { App.Toast("Export failed", ex.Message, ToastKind.Error); }
    }

    // ================= overlays =================

    private bool _settingsOpen, _aboutOpen;
    public bool SettingsOpen { get => _settingsOpen; set => Set(ref _settingsOpen, value); }
    public bool AboutOpen { get => _aboutOpen; set => Set(ref _aboutOpen, value); }
    public bool HasGitHub => AppInfo.GitHubUrl.Length > 0;
    public string AboutName => AppInfo.Name;
    public string AboutVersion => AppInfo.Version;
    public string AboutTagline => AppInfo.Tagline;
    public string AboutAuthor => AppInfo.Author;
    public string AboutEmail => AppInfo.Email;
    public string AboutGitHub => AppInfo.GitHubUrl;
    public string AboutPlatform => $"{AppInfo.Version}  ·  {AppInfo.Platform}";
    public string AboutRuntime => AppInfo.Runtime;
    public string AboutLicense => AppInfo.License;
    public string AboutWebsite => AppInfo.Website;
    public string AboutLicenseSummary => AppInfo.LicenseSummary;
    public string AboutCopyright => AppInfo.Copyright;
    public string DataFolder => DataPaths.Dir;

    public bool ThemeSystem { get => Settings.Theme == "System"; set { if (value) SetTheme("System"); } }
    public bool ThemeDark { get => Settings.Theme == "Dark"; set { if (value) SetTheme("Dark"); } }
    public bool ThemeLight { get => Settings.Theme == "Light"; set { if (value) SetTheme("Light"); } }
    private void SetTheme(string t)
    {
        Settings.Theme = t; Settings.Save(); App.ApplyTheme(t);
        Raise(nameof(ThemeSystem)); Raise(nameof(ThemeDark)); Raise(nameof(ThemeLight));
    }
    public int MetricsIndex
    {
        get => Settings.MetricsIntervalSec switch { 1 => 0, 2 => 1, 5 => 2, _ => 3 };
        set { Settings.MetricsIntervalSec = value switch { 0 => 1, 1 => 2, 2 => 5, _ => 10 }; ApplyIntervals(); Raise(); }
    }
    public bool CloseToMenuBar { get => Settings.CloseToMenuBar; set { Settings.CloseToMenuBar = value; Raise(); } }
    public bool ConfirmDestructive { get => Settings.ConfirmDestructive; set { Settings.ConfirmDestructive = value; Raise(); } }

    private DialogModel? _dialog;
    public DialogModel? Dialog { get => _dialog; private set { if (Set(ref _dialog, value)) Raise(nameof(HasDialog)); } }
    public bool HasDialog => _dialog != null;

    public async Task<bool> ConfirmAsync(string title, string message, string ok, bool danger = false)
    {
        Dialog?.Result.TrySetResult(false);
        var d = new DialogModel { Title = title, Message = message, OkText = ok, Danger = danger };
        Dialog = d;
        App.EnsureMainWindowVisible();
        try { return await d.Result.Task; }
        finally { if (Dialog == d) Dialog = null; }
    }
    public void CloseDialog(bool result) => Dialog?.Result.TrySetResult(result);

    // ================= shell helpers =================

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            if (OperatingSystem.IsMacOS()) Process.Start("open", url);
            else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private static void Reveal(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var psi = new ProcessStartInfo("open");
                if (File.Exists(path)) psi.ArgumentList.Add("-R");
                psi.ArgumentList.Add(path);
                Process.Start(psi);
            }
            else if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"");
            else Process.Start("xdg-open", Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path);
        }
        catch (Exception ex) { App.Toast("Could not open", ex.Message, ToastKind.Error); }
    }
}
