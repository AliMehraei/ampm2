using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Ampm2.Pm2;
using Ampm2.Sys;

namespace Ampm2.Ui;

public enum ConnState { Connecting, Connected, NotRunning, AccessDenied, NoPm2, Error }

public sealed class DialogModel
{
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string OkText { get; init; } = "OK";
    public string CancelText { get; init; } = "Cancel";
    public bool Danger { get; init; }
    public bool ShowCancel { get; init; } = true;
    public TaskCompletionSource<bool> Result { get; } = new();
}

public sealed partial class MainViewModel : ObservableObject
{
    public static readonly string RpcPipe = Pm2Endpoints.Rpc;
    public static readonly string PubPipe = Pm2Endpoints.Pub;

    public Settings Settings { get; }
    public ObservableCollection<ProcessItem> Items { get; } = new();
    public ListCollectionView View { get; }
    public ObservableCollection<LogLine> Logs { get; } = new();
    public ObservableCollection<Toast> Toasts { get; } = new();

    private Pm2Rpc? _rpc;
    private Pm2Bus? _bus;
    private readonly ProcessMetrics _metrics = new();
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _metricsTimer, _listTimer, _retryTimer, _eventDebounce, _logFlush;
    private readonly ConcurrentQueue<LogEvent> _pendingLogs = new();
    private bool _connecting, _visible = true, _refreshing, _refreshAgain;

    public MainViewModel(Settings settings)
    {
        Settings = settings;
        View = new ListCollectionView(Items) { Filter = FilterItem, CustomSort = new ItemComparer(this) };

        _metricsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(Math.Clamp(settings.MetricsIntervalSec, 1, 30)) };
        _metricsTimer.Tick += (_, _) => SampleMetrics();
        _listTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(Math.Clamp(settings.ListIntervalSec, 10, 3600)) };
        _listTimer.Tick += async (_, _) => { await RefreshListAsync(); CheckStrays(); };
        _retryTimer = new DispatcherTimer(DispatcherPriority.Background);
        _retryTimer.Tick += async (_, _) => { _retryTimer.Stop(); await ConnectAsync(); };
        _eventDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
        _eventDebounce.Tick += async (_, _) => { _eventDebounce.Stop(); await RefreshListAsync(); };
        _logFlush = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _logFlush.Tick += (_, _) => FlushLogs();

        ThemeManager.Changed += () => { foreach (var i in Items) i.ThemeChanged(); foreach (var s in SavedItems) s.ThemeChanged(); };

        RefreshCommand = new AsyncCommand(async () => { if (State == ConnState.Connected) await RefreshListAsync(); else await ConnectAsync(); });
        StartCommand = new AsyncCommand(p => ActAsync(p, "start"), p => IsConnected);
        StopCommand = new AsyncCommand(p => ActAsync(p, "stop"), p => IsConnected);
        RestartCommand = new AsyncCommand(p => ActAsync(p, "restart"), p => IsConnected);
        ReloadCommand = new AsyncCommand(p => ActAsync(p, "reload"), p => IsConnected);
        DeleteCommand = new AsyncCommand(p => ActAsync(p, "delete"), p => IsConnected);
        ResetCommand = new AsyncCommand(p => ActAsync(p, "reset"), p => IsConnected);
        FlushCommand = new AsyncCommand(p => FlushAsync(p), p => IsConnected);
        RestartAllCommand = new AsyncCommand(() => AllAsync("restart"), () => IsConnected && Items.Count > 0);
        StopAllCommand = new AsyncCommand(() => AllAsync("stop"), () => IsConnected && Items.Count > 0);
        SaveCommand = new AsyncCommand(() => CliAsync("Saved", "Process list saved to dump.pm2.", "save"), () => IsConnected);
        ResurrectCommand = new AsyncCommand(() => CliAsync("Resurrected", "Saved processes restored.", "resurrect"), () => CanUseCli);
        StartDaemonCommand = new AsyncCommand(StartDaemonAsync, () => State == ConnState.NotRunning && CliTargetsSameDaemon);
        KillDaemonCommand = new AsyncCommand(KillDaemonAsync, () => IsConnected);
        RelaunchElevatedCommand = new RelayCommand(p => RelaunchElevated(p as string == "remember"));
        CleanStraysCommand = new AsyncCommand(CleanStraysAsync, () => StrayCount > 0);
        InstallNodeCommand = new AsyncCommand(InstallNodeAsync, () => !Installing);
        InstallPm2Command = new AsyncCommand(InstallPm2Async, () => !Installing && HasNode);
        OpenPathCommand = new RelayCommand(p => OpenPath(p as string));
        OpenFileCommand = new RelayCommand(p => OpenFile(p as string));
        CopyCommand = new RelayCommand(p => { try { if (p is string s && s.Length > 0) { Clipboard.SetText(s); App.Toast("Copied", s.Length > 60 ? s[..60] + "…" : s, ToastKind.Info); } } catch { } });
        SortCommand = new RelayCommand(p => SetSort(p as string ?? "id"));
        ClearLogsCommand = new RelayCommand(() => Logs.Clear());
        ReloadLogsCommand = new RelayCommand(LoadLogHistory);
        ShowLogsCommand = new RelayCommand(p =>
        {
            if (p is ProcessItem i) { foreach (var x in Items) x.IsSelected = x == i; Selected = i; }
            if (Selected != null) RequestTab?.Invoke("logs");
        });
        AddCommand = new RelayCommand(() => App.ShowAddWindow(), () => CanUseCli);
        OpenSettingsCommand = new RelayCommand(() => SettingsOpen = true);
        CloseSettingsCommand = new RelayCommand(() => { SettingsOpen = false; Settings.Save(); });
        InitSaved();
        OpenAboutCommand = new RelayCommand(() => { SettingsOpen = false; AboutOpen = true; App.EnsureMainWindowVisible(); });
        CloseAboutCommand = new RelayCommand(() => AboutOpen = false);
        OpenEmailCommand = new RelayCommand(() => OpenUrl("mailto:" + AppInfo.Email + "?subject=ampm2%20" + AppInfo.Version));
        OpenGitHubCommand = new RelayCommand(() => OpenUrl(AppInfo.GitHubUrl), () => HasGitHub);
        OpenLicenseCommand = new RelayCommand(() => OpenUrl(AppInfo.LicenseUrl));
        OpenWebsiteCommand = new RelayCommand(() => OpenUrl(AppInfo.Website));
        CopyAboutCommand = new RelayCommand(() =>
        {
            var text = $"{AppInfo.Name} {AppInfo.Version} ({AppInfo.Platform}, {AppInfo.Runtime})\npm2 {(Pm2Version.Length > 0 ? Pm2Version : "not connected")}\n{AppInfo.Author} <{AppInfo.Email}>";
            try { Clipboard.SetText(text); App.Toast("Copied", "About details copied to the clipboard.", ToastKind.Info); } catch { }
        });
    }

    public ICommand ShowLogsCommand { get; }
    public ICommand OpenAboutCommand { get; }
    public ICommand CloseAboutCommand { get; }
    public ICommand OpenEmailCommand { get; }
    public ICommand OpenGitHubCommand { get; }
    public ICommand OpenLicenseCommand { get; }
    public ICommand OpenWebsiteCommand { get; }
    public ICommand CopyAboutCommand { get; }
    public bool HasGitHub => AppInfo.GitHubUrl.Length > 0;
    private bool _aboutOpen;
    public bool AboutOpen { get => _aboutOpen; set => Set(ref _aboutOpen, value); }
    public ICommand AddCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    /// <summary>Asks the view to switch the detail tab (the tab radio buttons own the state).</summary>
    public event Action<string>? RequestTab;

    private bool _settingsOpen;
    public bool SettingsOpen { get => _settingsOpen; set => Set(ref _settingsOpen, value); }

    public string Pm2Home => Environment.GetEnvironmentVariable("PM2_HOME") is { Length: > 0 } h ? h
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pm2");
    public string AppVersion => AppInfo.Version;

    private int _selectedCount;
    public int SelectedCount { get => _selectedCount; private set { if (Set(ref _selectedCount, value)) Raise(nameof(HasMultiSelection)); } }
    public bool HasMultiSelection => IsProcessView && _selectedCount > 1;
    public void UpdateSelectionCount() => SelectedCount = Items.Count(i => i.IsSelected);

    // ---------------- connection ----------------

    private ConnState _state = ConnState.Connecting;
    public ConnState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value)) return;
            Raise(nameof(IsConnected)); Raise(nameof(StateText)); Raise(nameof(CanUseCli));
            Raise(nameof(ShowNotRunning)); Raise(nameof(ShowAccessDenied)); Raise(nameof(ShowNoPm2)); Raise(nameof(ShowError));
            Raise(nameof(ShowList)); Raise(nameof(HasNode)); Raise(nameof(ShowConnecting)); Raise(nameof(IsEmpty));
            RefreshSavedStates();
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool IsConnected => State == ConnState.Connected;
    public bool ShowNotRunning => IsProcessView && State == ConnState.NotRunning;
    public bool ShowAccessDenied => IsProcessView && State == ConnState.AccessDenied;
    public bool ShowNoPm2 => IsProcessView && State == ConnState.NoPm2;
    public bool ShowError => IsProcessView && State == ConnState.Error;
    public bool ShowConnecting => IsProcessView && State == ConnState.Connecting;
    public bool ShowList => IsProcessView && State == ConnState.Connected;
    /// <summary>
    /// The pm2 CLI is only safe to run when this process can reach the daemon (or there is no daemon yet):
    /// otherwise every pm2 command spawns a stray daemon that cannot bind the pipe.
    /// </summary>
    public bool CanUseCli => State is ConnState.Connected or ConnState.NotRunning && CliTargetsSameDaemon;
    /// <summary>With the pipes overridden (testing), the stock pm2 CLI would hit the real daemon; require a matching AMPM2_PM2 shim.</summary>
    private static bool CliTargetsSameDaemon => Pm2Endpoints.IsDefault || Environment.GetEnvironmentVariable("AMPM2_PM2") is { Length: > 0 };
    public bool IsElevated { get; } = Elevation.IsElevated;
    public bool HasNode => Pm2Cli.NodePath != null;

    private string _errorText = "";
    public string ErrorText { get => _errorText; private set => Set(ref _errorText, value); }

    private string _version = "";
    public string Pm2Version { get => _version; private set { if (Set(ref _version, value)) Raise(nameof(StateText)); } }
    private int _daemonPid;
    public int DaemonPid { get => _daemonPid; private set { if (Set(ref _daemonPid, value)) Raise(nameof(StateText)); } }

    public string StateText => State switch
    {
        ConnState.Connected => $"pm2 {(Pm2Version.Length > 0 ? "v" + Pm2Version : "")} · daemon pid {DaemonPid}".Replace("  ", " "),
        ConnState.Connecting => "Connecting…",
        ConnState.NotRunning => "Daemon not running",
        ConnState.AccessDenied => "Daemon is elevated",
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
            if (!PipeConnection.PipeExists(RpcPipe))
            {
                State = Pm2Cli.Pm2Path == null ? ConnState.NoPm2 : ConnState.NotRunning;
                Retry(5);
                return;
            }
            if (State != ConnState.AccessDenied) State = ConnState.Connecting;
            Pm2Rpc rpc;
            try { rpc = await Pm2Rpc.ConnectAsync(RpcPipe, CancellationToken.None); }
            catch (PipeConnectException ex)
            {
                State = ex.Kind switch { ConnectFailure.AccessDenied => ConnState.AccessDenied, ConnectFailure.NotRunning => ConnState.NotRunning, _ => ConnState.Error };
                ErrorText = ex.Message;
                Retry(ex.Kind == ConnectFailure.AccessDenied ? 15 : 5);
                return;
            }
            _rpc = rpc;
            rpc.Disconnected += _ => _ui.BeginInvoke(() => OnDisconnected(rpc));
            try
            {
                var bus = await Pm2Bus.ConnectAsync(PubPipe, CancellationToken.None);
                bus.ProcessEvent += (_, _) => _ui.BeginInvoke(() => { _eventDebounce.Stop(); _eventDebounce.Start(); });
                bus.Log += e => { _pendingLogs.Enqueue(e); };
                bus.LogFilter = _logTarget;
                _bus = bus;
            }
            catch { /* live events are optional; the backstop timer still refreshes */ }

            DaemonPid = rpc.DaemonPid;
            Pm2Version = await rpc.GetVersionAsync();
            State = ConnState.Connected;
            await RefreshListAsync();
            CheckStrays();
            UpdateTimers();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            State = ConnState.Error;
            Retry(5);
        }
        finally { _connecting = false; }
    }

    private void Retry(int seconds)
    {
        _retryTimer.Interval = TimeSpan.FromSeconds(seconds);
        _retryTimer.Start();
    }

    private void OnDisconnected(Pm2Rpc rpc)
    {
        if (_rpc != rpc) return;
        _rpc = null;
        try { _bus?.Dispose(); } catch { }
        _bus = null;
        try { rpc.Dispose(); } catch { }
        State = ConnState.Connecting;
        foreach (var i in Items) i.ClearMetrics();
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

    // ---------------- list ----------------

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
                catch (Exception ex) when (ex is not Pm2Exception) { return; }   // disconnect handler takes over
                Merge(list);
            } while (_refreshAgain);
        }
        finally { _refreshing = false; }
    }

    private void Merge(List<Pm2Process> list)
    {
        var byId = Items.ToDictionary(i => i.Id);
        var seen = new HashSet<int>();
        foreach (var p in list)
        {
            seen.Add(p.Id);
            if (byId.TryGetValue(p.Id, out var item)) item.Update(p);
            else Items.Add(new ProcessItem(p));
        }
        for (int i = Items.Count - 1; i >= 0; i--)
            if (!seen.Contains(Items[i].Id))
            {
                if (Selected == Items[i]) Selected = null;
                Items.RemoveAt(i);
            }
        View.Refresh();
        if (Selected == null && Items.Count > 0 && _autoSelect) { _autoSelect = false; Selected = View.Cast<ProcessItem>().FirstOrDefault(); }
        SampleMetrics();
        RaiseCounts();
        Raise(nameof(IsEmpty));
        Raise(nameof(SelectedDetailsChanged));
        SyncLibrary(false);
        CommandManager.InvalidateRequerySuggested();
    }
    private bool _autoSelect = true;

    public bool IsEmpty => IsProcessView && IsConnected && Items.Count == 0;

    private void SampleMetrics()
    {
        if (Items.Count == 0) return;
        var roots = new List<int>();
        foreach (var i in Items) if (i.IsOnline && i.P.Pid > 0) roots.Add(i.P.Pid);
        var usage = _metrics.Sample(roots);
        foreach (var i in Items)
        {
            if (i.IsOnline && usage.TryGetValue(i.P.Pid, out var u)) i.ApplyMetrics(u.CpuPercent, u.MemoryBytes, u.ProcessCount, true);
            else if (!i.IsOnline) i.ClearMetrics();
            i.Tick();
        }
        Selected?.BuildSparklines(360, 56);
        if (_sortKey is "cpu" or "mem") View.Refresh();
        RaiseTotals();
    }

    private int _online, _stopped, _errored;
    public int OnlineCount { get => _online; private set => Set(ref _online, value); }
    public int StoppedCount { get => _stopped; private set => Set(ref _stopped, value); }
    public int ErroredCount { get => _errored; private set => Set(ref _errored, value); }
    public int TotalCount => Items.Count;

    private void RaiseCounts()
    {
        OnlineCount = Items.Count(i => i.StatusKey == "Online");
        StoppedCount = Items.Count(i => i.StatusKey == "Stopped");
        ErroredCount = Items.Count(i => i.StatusKey == "Errored");
        Raise(nameof(TotalCount));
    }

    private string _totalCpu = "—", _totalMem = "—";
    public string TotalCpuText { get => _totalCpu; private set => Set(ref _totalCpu, value); }
    public string TotalMemText { get => _totalMem; private set => Set(ref _totalMem, value); }
    private void RaiseTotals()
    {
        double c = 0; long m = 0;
        foreach (var i in Items) if (i.IsOnline) { c += i.Cpu; m += i.Memory; }
        TotalCpuText = $"{c:0.#}%";
        TotalMemText = Fmt.Bytes(m);
    }

    // ---------------- filter / sort ----------------

    private string _search = "";
    public string SearchText { get => _search; set { if (Set(ref _search, value)) View.Refresh(); } }
    private string _statusFilter = "all";
    public string StatusFilter { get => _statusFilter; set { if (Set(ref _statusFilter, value)) View.Refresh(); } }

    private bool FilterItem(object o)
    {
        var i = (ProcessItem)o;
        if (_statusFilter != "all" && !string.Equals(i.StatusKey, _statusFilter, StringComparison.OrdinalIgnoreCase)) return false;
        if (_search.Length == 0) return true;
        return i.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) || i.IdText == _search ||
               i.P.Script.Contains(_search, StringComparison.OrdinalIgnoreCase) || i.Namespace.Contains(_search, StringComparison.OrdinalIgnoreCase);
    }

    private string _sortKey = "id";
    private bool _sortDesc;
    public string SortKey => _sortKey;
    public bool SortDesc => _sortDesc;
    private void SetSort(string key)
    {
        if (_sortKey == key) _sortDesc = !_sortDesc;
        else { _sortKey = key; _sortDesc = key is "cpu" or "mem" or "restarts" or "uptime"; }
        Raise(nameof(SortKey)); Raise(nameof(SortDesc)); Raise(nameof(SortGlyph));
        View.Refresh();
    }
    public string SortGlyph => _sortDesc ? "" : "";

    private sealed class ItemComparer : System.Collections.IComparer
    {
        private readonly MainViewModel _vm;
        public ItemComparer(MainViewModel vm) => _vm = vm;
        public int Compare(object? x, object? y)
        {
            var a = (ProcessItem)x!; var b = (ProcessItem)y!;
            int r = _vm._sortKey switch
            {
                "name" => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                "status" => string.Compare(a.StatusKey, b.StatusKey, StringComparison.Ordinal),
                "cpu" => a.Cpu.CompareTo(b.Cpu),
                "mem" => a.Memory.CompareTo(b.Memory),
                "restarts" => a.Restarts.CompareTo(b.Restarts),
                "uptime" => (a.IsOnline ? -a.P.UptimeSince : long.MinValue).CompareTo(b.IsOnline ? -b.P.UptimeSince : long.MinValue),
                _ => 0,
            };
            if (r == 0) r = a.Id.CompareTo(b.Id);
            return _vm._sortDesc ? -r : r;
        }
    }

    // ---------------- selection / details ----------------

    private ProcessItem? _selected;
    public ProcessItem? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            Raise(nameof(HasSelection)); Raise(nameof(ShowProcessDetail)); Raise(nameof(ShowNoSelection));
            Raise(nameof(SelectedDetailsChanged));
            value?.BuildSparklines(360, 56);
            if (LogsVisible) SwitchLogTarget();
        }
    }
    public bool HasSelection => _selected != null;
    public object? SelectedDetailsChanged => null;

    private string _detailTab = "overview";
    public string DetailTab
    {
        get => _detailTab;
        set
        {
            if (!Set(ref _detailTab, value)) return;
            Raise(nameof(LogsVisible));
            if (LogsVisible) SwitchLogTarget();
            else { SetLogTarget(-1); Logs.Clear(); }
            UpdateTimers();
        }
    }
    public bool LogsVisible => _detailTab == "logs";

    // ---------------- logs ----------------

    private int _logTarget = -1;
    private void SetLogTarget(int id)
    {
        _logTarget = id;
        if (_bus != null) _bus.LogFilter = id;
        while (_pendingLogs.TryDequeue(out _)) { }
    }

    private void SwitchLogTarget()
    {
        SetLogTarget(_selected?.Id ?? -1);
        LoadLogHistory();
    }

    private bool _followLogs = true;
    public bool FollowLogs { get => _followLogs; set => Set(ref _followLogs, value); }
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
            Logs.Add(new LogLine($"stdout · {s.P.OutLog}", LogKind.Marker));
            foreach (var l in LogFiles.Tail(s.P.OutLog, 300)) Logs.Add(new LogLine(l, LogKind.Out));
        }
        if (_logStream != "out")
        {
            Logs.Add(new LogLine($"stderr · {s.P.ErrLog}", LogKind.Marker));
            foreach (var l in LogFiles.Tail(s.P.ErrLog, 300)) Logs.Add(new LogLine(l, LogKind.Err));
        }
        Logs.Add(new LogLine(_bus != null ? "live" : "live stream unavailable — use reload", LogKind.Marker));
        LogsAppended?.Invoke(true);
    }

    private void FlushLogs()
    {
        if (_pendingLogs.IsEmpty) return;
        if (_pauseLogs) { while (_pendingLogs.Count > 5000 && _pendingLogs.TryDequeue(out _)) { } return; }
        int added = 0;
        while (_pendingLogs.TryDequeue(out var e) && added < 2000)
        {
            if (e.ProcessId != _logTarget) continue;
            if (e.IsError ? _logStream == "out" : _logStream == "err") continue;
            var time = e.At.ToString("HH:mm:ss");
            foreach (var raw in e.Text.Split('\n'))
            {
                var t = Pm2Cli.StripAnsi(raw.TrimEnd('\r'));
                if (t.Length == 0) continue;
                Logs.Add(new LogLine(t, e.IsError ? LogKind.Err : LogKind.Out, time));
                added++;
            }
        }
        const int cap = 4000;
        if (Logs.Count > cap + 500) { while (Logs.Count > cap) Logs.RemoveAt(0); }
        if (added > 0) LogsAppended?.Invoke(false);
    }

    // ---------------- actions ----------------

    public ICommand RefreshCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand FlushCommand { get; }
    public ICommand RestartAllCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ResurrectCommand { get; }
    public ICommand StartDaemonCommand { get; }
    public ICommand KillDaemonCommand { get; }
    public ICommand RelaunchElevatedCommand { get; }
    public ICommand CleanStraysCommand { get; }
    public ICommand InstallNodeCommand { get; }
    public ICommand InstallPm2Command { get; }
    public ICommand OpenPathCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand SortCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand ReloadLogsCommand { get; }

    /// <summary>Targets: the row passed as parameter, or every checked row, or the selected row.</summary>
    private List<ProcessItem> Targets(object? p)
    {
        if (p is ProcessItem one) return new List<ProcessItem> { one };
        var sel = Items.Where(i => i.IsSelected).ToList();
        if (sel.Count > 0) return sel;
        return _selected != null ? new List<ProcessItem> { _selected } : new List<ProcessItem>();
    }

    private static string Past(string verb) => verb switch
    {
        "start" => "Started", "stop" => "Stopped", "restart" => "Restarted", "reload" => "Reloaded",
        "delete" => "Deleted", "reset" => "Counters reset", _ => verb,
    };

    private Task ActAsync(object? p, string verb) => ActOnAsync(Targets(p), verb);

    private async Task ActOnAsync(List<ProcessItem> targets, string verb)
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
                        case "reset": await rpc.ResetAsync(t.Id); break;
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
                $"This will {verb} all {Items.Count} pm2 processes.", verb == "stop" ? "Stop all" : "Restart all", verb == "stop"))
            return;
        await ActOnAsync(Items.ToList(), verb);
    }

    private async Task FlushAsync(object? p)
    {
        var t = p as string == "all" ? new List<ProcessItem>() : Targets(p);
        var args = new List<string> { "flush" };
        if (t.Count == 1) args.Add(t[0].Id.ToString());
        if (Settings.ConfirmDestructive && !await ConfirmAsync(t.Count == 1 ? $"Empty the logs of '{t[0].Name}'?" : "Empty all pm2 logs?",
                "The log files are truncated. This cannot be undone.", "Flush", true)) return;
        await CliAsync("Logs flushed", t.Count == 1 ? t[0].Name : "All processes", args.ToArray());
        if (LogsVisible) LoadLogHistory();
    }

    public async Task<bool> CliAsync(string okTitle, string okMessage, params string[] args)
    {
        if (!CanUseCli)
        {
            App.Toast("Not available", "ampm2 cannot reach the pm2 daemon, and running pm2 now would only spawn a stray daemon.", ToastKind.Error);
            return false;
        }
        if (Pm2Cli.Pm2Path == null) { App.Toast("pm2 not found", "Install pm2 first.", ToastKind.Error); return false; }
        CliBusy = true;
        try
        {
            var r = await Pm2Cli.Pm2Async(args);
            if (r.Ok) App.Toast(okTitle, okMessage, ToastKind.Success);
            else App.Toast($"pm2 {args[0]} failed", Tail(r.Output, 6), ToastKind.Error);
            if (IsConnected) await RefreshListAsync(); else await ConnectAsync();
            return r.Ok;
        }
        finally { CliBusy = false; }
    }

    private bool _cliBusy;
    public bool CliBusy { get => _cliBusy; private set => Set(ref _cliBusy, value); }

    private static string Tail(string s, int lines)
    {
        var l = s.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("\n", l.Skip(Math.Max(0, l.Length - lines))).Trim();
    }

    private async Task StartDaemonAsync()
    {
        CliBusy = true;
        try
        {
            var r = await Pm2Cli.Pm2Async(new[] { "ping" });
            if (!r.Ok) App.Toast("Could not start pm2", Tail(r.Output, 6), ToastKind.Error);
        }
        finally { CliBusy = false; }
        await ConnectAsync();
        if (IsConnected && Items.Count == 0 &&
            await ConfirmAsync("Restore saved processes?", "The pm2 daemon is running. Do you want to resurrect the process list saved in dump.pm2?", "Resurrect"))
            await CliAsync("Resurrected", "Saved processes restored.", "resurrect");
    }

    private async Task KillDaemonAsync()
    {
        if (!await ConfirmAsync("Stop the pm2 daemon?", $"All {Items.Count} processes managed by pm2 will be stopped and pm2 itself exits.\nUse Save first if you want to restore them later.", "Kill pm2", true))
            return;
        await CliAsync("pm2 stopped", "The daemon and its processes were stopped.", "kill");
    }

    private void RelaunchElevated(bool remember)
    {
        if (Elevation.RelaunchElevated(remember ? "--register-task" : ""))
            App.ExitApp();
        else
            App.Toast("Cancelled", "Administrator rights were not granted.", ToastKind.Info);
    }

    // ---------------- stray daemons ----------------

    private List<StrayDaemon> _strays = new();
    private int _strayCount;
    public int StrayCount { get => _strayCount; private set { if (Set(ref _strayCount, value)) { Raise(nameof(HasStrays)); Raise(nameof(StrayText)); CommandManager.InvalidateRequerySuggested(); } } }
    public bool HasStrays => _strayCount > 0;
    private long _strayMem;
    public string StrayText => $"{_strayCount} stray pm2 daemon{(_strayCount == 1 ? "" : "s")} · {Fmt.Bytes(_strayMem)}";

    private async void CheckStrays()
    {
        int pid = DaemonPid;
        if (pid <= 0) return;
        var list = await Task.Run(() => StrayDaemons.Find(pid));
        _strays = list;
        _strayMem = list.Sum(s => s.MemoryBytes);
        StrayCount = list.Count;
        Raise(nameof(StrayText));
    }

    private async Task CleanStraysAsync()
    {
        int pid = DaemonPid;
        if (pid <= 0) return;
        var list = await Task.Run(() => StrayDaemons.Find(pid));
        if (list.Count == 0) { StrayCount = 0; return; }
        if (!await ConfirmAsync($"End {list.Count} stray pm2 daemon{(list.Count == 1 ? "" : "s")}?",
                $"These node processes run pm2's Daemon.js but are not the daemon ampm2 is connected to (pid {pid}) and manage no processes. " +
                $"They were left behind by pm2 commands that could not reach the real daemon, usually a normal terminal while pm2 runs as administrator.\n\n" +
                $"Frees about {Fmt.Bytes(list.Sum(s => s.MemoryBytes))}.", "End them", true))
            return;
        int n = await Task.Run(() => StrayDaemons.Kill(list, pid));
        App.Toast("Cleaned up", $"Ended {n} of {list.Count} stray daemons.", n == list.Count ? ToastKind.Success : ToastKind.Error);
        CheckStrays();
    }

    // ---------------- install ----------------

    private bool _installing;
    public bool Installing { get => _installing; private set { if (Set(ref _installing, value)) CommandManager.InvalidateRequerySuggested(); } }
    private string _installLog = "";
    public string InstallLog { get => _installLog; private set => Set(ref _installLog, value); }

    private async Task InstallNodeAsync()
    {
        var winget = Pm2Cli.WingetPath;
        if (winget == null)
        {
            App.Toast("winget not available", "Opening the Node.js download page instead.", ToastKind.Info);
            OpenUrl("https://nodejs.org/en/download");
            return;
        }
        Installing = true;
        InstallLog = "Installing Node.js LTS with winget… (Windows may ask for permission)";
        try
        {
            var r = await Pm2Cli.RunAsync(winget, new[] { "install", "--id", "OpenJS.NodeJS.LTS", "-e", "--silent", "--accept-source-agreements", "--accept-package-agreements" });
            InstallLog = Tail(r.Output, 8);
            App.Toast(r.Ok ? "Node.js installed" : "Node.js install failed", r.Ok ? "Now install pm2." : Tail(r.Output, 3), r.Ok ? ToastKind.Success : ToastKind.Error);
        }
        finally { Installing = false; Raise(nameof(HasNode)); }
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

    // ---------------- shell helpers ----------------

    private static void OpenPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
            else App.Toast("Not found", path, ToastKind.Error);
        }
        catch (Exception ex) { App.Toast("Could not open", ex.Message, ToastKind.Error); }
    }

    private static void OpenFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (!File.Exists(path)) { App.Toast("Not found", path, ToastKind.Error); return; }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            try { Process.Start("notepad.exe", $"\"{path}\""); } catch (Exception ex) { App.Toast("Could not open", ex.Message, ToastKind.Error); }
        }
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // ---------------- dialog ----------------

    private DialogModel? _dialog;
    public DialogModel? Dialog { get => _dialog; private set { if (Set(ref _dialog, value)) Raise(nameof(HasDialog)); } }
    public bool HasDialog => _dialog != null;

    public async Task<bool> ConfirmAsync(string title, string message, string ok, bool danger = false)
    {
        Dialog?.Result.TrySetResult(false);
        var d = new DialogModel { Title = title, Message = message, OkText = ok, Danger = danger };
        Dialog = d;
        App.EnsureMainWindowVisible();   // never steals focus from another app when already open
        try { return await d.Result.Task; }
        finally { if (Dialog == d) Dialog = null; }
    }

    public void CloseDialog(bool result) => Dialog?.Result.TrySetResult(result);
}
