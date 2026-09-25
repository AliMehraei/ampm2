using System;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Ampm2.Sys;
using Ampm2.Ui;

namespace Ampm2;

public partial class App : Application
{
    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;
    private TrayIcon? _tray;
    private IntPtr _trayIcon;
    private Logo.Badge _trayBadge = (Logo.Badge)(-1);
    public static MainViewModel? Vm { get; private set; }
    public static Settings AppSettings { get; private set; } = new();
    private static MainWindow? _window;
    public static bool Exiting { get; private set; }
    private static string HiddenMarker => System.IO.Path.Combine(Settings.Dir, "start-hidden");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        Updater.Target = UpdateTarget.WindowsInstaller;
        DispatcherUnhandledException += (_, ex) =>
        {
            LogCrash(ex.Exception);
            ex.Handled = true;
            Toast("Unexpected error", ex.Exception.Message, ToastKind.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => LogCrash(ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) => { LogCrash(ex.Exception); ex.SetObserved(); };

        // Build helper: ampm2.exe --export-icon <path>
        int ei = Array.IndexOf(args, "--export-icon");
        if (ei >= 0 && ei + 1 < args.Length) { Logo.WriteIco(args[ei + 1]); Shutdown(0); return; }
        // Build helper for the macOS app: ampm2.exe --export-pngs <dir>  (icon-<size>.png, used for the .icns and the menu bar)
        int ep = Array.IndexOf(args, "--export-pngs");
        if (ep >= 0 && ep + 1 < args.Length)
        {
            System.IO.Directory.CreateDirectory(args[ep + 1]);
            foreach (var size in new[] { 16, 32, 44, 64, 128, 256, 512, 1024 })
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(args[ep + 1], $"icon-{size}.png"), Logo.Png(size));
            Shutdown(0);
            return;
        }
        // Installer helper (runs elevated): point an existing "no UAC prompt" task at this exe.
        if (args.Contains("--repoint-task"))
        {
            int code = 0;
            if (Elevation.IsElevated && await Elevation.ElevatedTaskExistsAsync() && !await Elevation.ElevatedTaskIsMineAsync())
                code = (await Elevation.RegisterElevatedTaskAsync()).Ok ? 0 : 1;
            Shutdown(code);
            return;
        }
        int st = Array.IndexOf(args, "--selftest");
        if (st >= 0 && st + 1 < args.Length) { Shutdown(await SelfTest.RunAsync(args[st + 1])); return; }

        bool handedOver = args.Contains("--relaunched") || args.Contains("--from-task");
        _mutex = new Mutex(false, @"Local\ampm2-single-instance" + Settings.Profile);
        bool owns = false;
        try { owns = _mutex.WaitOne(handedOver ? 8000 : 0); } catch (AbandonedMutexException) { owns = true; }
        if (!owns)
        {
            try { EventWaitHandle.OpenExisting(@"Local\ampm2-show" + Settings.Profile).Set(); } catch { }
            Shutdown(0);
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ampm2-show" + Settings.Profile);
        var showThread = new Thread(() =>
        {
            while (_showEvent.WaitOne()) Dispatcher.BeginInvoke(ShowMainWindow);
        }) { IsBackground = true, Name = "ampm2-show" };
        showThread.Start();

        AppSettings = Settings.Load();
        AppLibrary.Warning += (t, m) => Toast(t, m, ToastKind.Error);

        // The daemon is elevated, we are not, and the user opted into the no-prompt task: hand over and exit.
        if (!Elevation.IsElevated && !handedOver && DaemonDeniesUs() && await Elevation.ElevatedTaskIsMineAsync())
        {
            // the task has fixed arguments, so tell the elevated copy to stay in the tray via a marker file
            if (args.Contains("--autostart")) try { System.IO.Directory.CreateDirectory(Settings.Dir); System.IO.File.WriteAllText(HiddenMarker, ""); } catch { }
            var r = await Elevation.RunElevatedTaskAsync();
            if (r.Ok) { _mutex.ReleaseMutex(); Shutdown(0); return; }
        }

        // Software rendering: ~50 MB less private memory than the D3D pipeline, and no more CPU for this mostly static UI.
        bool gpu = Environment.GetEnvironmentVariable("AMPM2_RENDER") is { } rm ? rm != "software" : AppSettings.GpuRendering;
        System.Windows.Media.RenderOptions.ProcessRenderMode = gpu
            ? System.Windows.Interop.RenderMode.Default : System.Windows.Interop.RenderMode.SoftwareOnly;
        ThemeManager.Apply(AppSettings.Theme);
        ThemeManager.HookSystemChanges();

        Vm = new MainViewModel(AppSettings);
        Vm.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName is nameof(MainViewModel.ErroredCount) or nameof(MainViewModel.OnlineCount) or nameof(MainViewModel.State)
                or nameof(MainViewModel.StoppedCount))
                UpdateTray();
        };

        _window = new MainWindow(Vm);
        CreateTray();

        if (args.Contains("--register-task") && Elevation.IsElevated)
        {
            var r = await Elevation.RegisterElevatedTaskAsync();
            Toast(r.Ok ? "Elevated launch enabled" : "Could not create the task", r.Ok ? "ampm2 now starts as administrator without a UAC prompt." : r.Output, r.Ok ? ToastKind.Success : ToastKind.Error);
        }

        bool marker = false;
        try { if (System.IO.File.Exists(HiddenMarker)) { System.IO.File.Delete(HiddenMarker); marker = true; } } catch { }
        bool hidden = (AppSettings.StartMinimized || args.Contains("--autostart") || marker) && !args.Contains("--show");
        if (!hidden) ShowMainWindow();
        else Vm.SetVisible(false);

        if (args.Contains("--updated")) Toast("Updated", $"ampm2 was updated to {AppInfo.Version}.", ToastKind.Success);

        await Vm.ConnectAsync();
        UpdateTray();
    }

    /// <summary>Quick probe: the rpc pipe exists but refuses us (daemon runs as administrator).</summary>
    private static bool DaemonDeniesUs()
    {
        try
        {
            using var p = new NamedPipeClientStream(".", MainViewModel.RpcPipe, PipeDirection.InOut);
            p.Connect(300);
            return false;
        }
        catch (UnauthorizedAccessException) { return true; }
        catch { return false; }
    }

    // ---------------- tray ----------------

    private void CreateTray()
    {
        _trayIcon = Logo.HIcon(32, Logo.Badge.None);
        _tray = new TrayIcon(_trayIcon, "ampm2");
        _tray.LeftClick += () => { if (_window is { IsVisible: true, WindowState: not WindowState.Minimized } && _window.IsActive) HideMainWindow(); else ShowMainWindow(); };
        _tray.RightClick += ShowTrayMenu;
        UpdateTray();
    }

    private void UpdateTray()
    {
        if (_tray == null || Vm == null) return;
        var badge = Vm.State != ConnState.Connected ? Logo.Badge.Warn : Vm.ErroredCount > 0 ? Logo.Badge.Error : Logo.Badge.None;
        string tip = Vm.State == ConnState.Connected
            ? $"ampm2 · {Vm.OnlineCount} online" + (Vm.StoppedCount > 0 ? $" · {Vm.StoppedCount} stopped" : "") + (Vm.ErroredCount > 0 ? $" · {Vm.ErroredCount} errored" : "")
            : "ampm2 · " + Vm.StateText;
        if (badge != _trayBadge)
        {
            var old = _trayIcon;
            _trayIcon = Logo.HIcon(32, badge);
            _trayBadge = badge;
            _tray.Update(_trayIcon, tip);
            if (old != IntPtr.Zero) Native.DestroyIcon(old);
        }
        else _tray.Update(_trayIcon, tip);
    }

    private void ShowTrayMenu()
    {
        if (Vm == null || _tray == null) return;
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        MenuItem Item(string header, string glyph, Action a, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, Tag = glyph, IsEnabled = enabled };
            mi.Click += (_, _) => a();
            return mi;
        }
        bool c = Vm.IsConnected;
        menu.Items.Add(Item("Open ampm2", "", ShowMainWindow));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Restart all", "", () => Vm.RestartAllCommand.Execute(null), c && Vm.TotalCount > 0));
        menu.Items.Add(Item("Stop all", "", () => Vm.StopAllCommand.Execute(null), c && Vm.TotalCount > 0));
        menu.Items.Add(Item("Save process list", "", () => Vm.SaveCommand.Execute(null), c));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Help", "\uE897", () => ShowHelp()));
        menu.Items.Add(Item("About ampm2", "\uE946", () => Vm.OpenAboutCommand.Execute(null)));
        menu.Items.Add(Item("Exit", "", ExitApp));
        Native.SetForegroundWindow(_tray.Handle);
        menu.IsOpen = true;
    }

    // ---------------- window ----------------

    public static void ShowMainWindow()
    {
        if (_window == null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true; _window.Topmost = false;
        Vm?.SetVisible(true);
    }

    /// <summary>Shows the window only if it is hidden or minimized (e.g. an action started from the tray).</summary>
    public static void EnsureMainWindowVisible()
    {
        if (_window == null) return;
        if (!_window.IsVisible || _window.WindowState == WindowState.Minimized) ShowMainWindow();
    }

    private static HelpWindow? _help;

    /// <summary>Opens (or focuses) the help window, optionally on a topic id from Ampm2.Help.HelpContent.</summary>
    public static void ShowHelp(string? topic = null)
    {
        if (_help == null || !_help.IsLoaded)
        {
            _help = new HelpWindow();
            if (_window != null && _window.IsVisible) _help.Owner = _window;
            _help.Closed += (_, _) => _help = null;
            _help.Show();
        }
        else
        {
            if (_help.WindowState == WindowState.Minimized) _help.WindowState = WindowState.Normal;
            _help.Activate();
        }
        _help.ShowTopic(topic);
    }

    public static void ShowAddWindow()
    {
        if (_window == null || Vm == null) return;
        ShowMainWindow();
        var w = new AddProcessWindow(Vm) { Owner = _window };
        w.ShowDialog();
    }

    public static void HideMainWindow()
    {
        if (_window == null) return;
        _window.Hide();
        Vm?.SetVisible(false);
        _window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, Native.TrimSelf);
    }

    public static void ExitApp()
    {
        Exiting = true;
        var app = (App)Current;
        AppSettings.Save();
        app._tray?.Dispose();
        if (app._trayIcon != IntPtr.Zero) Native.DestroyIcon(app._trayIcon);
        try { _mutex?.ReleaseMutex(); } catch { }
        app.Shutdown(0);
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            System.IO.Directory.CreateDirectory(Settings.Dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(Settings.Dir, "errors.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }

    // ---------------- toasts ----------------

    public static void Toast(string title, string message, ToastKind kind)
    {
        var vm = Vm;
        if (vm == null || Current == null) return;
        Current.Dispatcher.BeginInvoke(async () =>
        {
            var t = new Toast(title, message, kind);
            vm.Toasts.Add(t);
            while (vm.Toasts.Count > 4) vm.Toasts.RemoveAt(0);
            await Task.Delay(kind == ToastKind.Error ? 9000 : 4000);
            vm.Toasts.Remove(t);
        });
    }
}
