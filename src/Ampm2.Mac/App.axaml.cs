using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ampm2.Mac.ViewModels;
using Ampm2.Mac.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Ampm2.Mac;

public partial class App : Application
{
    public static MainViewModel? Vm { get; private set; }
    private static MainWindow? _window;
    public static bool Quitting { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = MacSettings.Load();
        ApplyTheme(settings.Theme);
        Vm = new MainViewModel(settings);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;   // stays in the menu bar when the window is closed
            _window = new MainWindow(Vm);
            desktop.MainWindow = _window;
            _window.Show();
            desktop.Exit += (_, _) => settings.Save();
        }
        // Dock icon clicked while the window is hidden
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime act)
            act.Activated += (_, e) => { if (e.Kind == ActivationKind.Reopen) ShowMainWindow(); };
        _ = Vm.ConnectAsync();
        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(string theme)
    {
        if (Current == null) return;
        Current.RequestedThemeVariant = theme switch { "Dark" => ThemeVariant.Dark, "Light" => ThemeVariant.Light, _ => ThemeVariant.Default };
    }

    // ---------------- window ----------------

    public static void ShowMainWindow()
    {
        if (_window == null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        Vm?.SetVisible(true);
    }

    public static void EnsureMainWindowVisible()
    {
        if (_window != null && (!_window.IsVisible || _window.WindowState == WindowState.Minimized)) ShowMainWindow();
    }

    public static void HideMainWindow()
    {
        _window?.Hide();
        Vm?.SetVisible(false);
    }

    public static void Quit()
    {
        Quitting = true;
        Vm?.Settings.Save();
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }

    public static async Task ShowAddWindowAsync()
    {
        if (_window == null || Vm == null) return;
        ShowMainWindow();
        await new AddProcessWindow(Vm).ShowDialog(_window);
    }

    private static HelpWindow? _help;

    /// <summary>Opens (or focuses) the help window, optionally on a topic id from Ampm2.Help.HelpContent.</summary>
    public static void ShowHelp(string? topic = null)
    {
        if (_help == null)
        {
            _help = new HelpWindow();
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

    public static void UpdateTrayTooltip()
    {
        var vm = Vm;
        if (vm == null || Current == null) return;
        var icons = TrayIcon.GetIcons(Current);
        if (icons == null) return;
        var tip = vm.IsConnected
            ? $"ampm2 · {vm.OnlineCount} online" + (vm.StoppedCount > 0 ? $" · {vm.StoppedCount} stopped" : "") + (vm.ErroredCount > 0 ? $" · {vm.ErroredCount} errored" : "")
            : "ampm2 · " + vm.StateText;
        foreach (var t in icons) t.ToolTipText = tip;
    }

    // ---------------- toasts / clipboard / files ----------------

    public static void Toast(string title, string message, ToastKind kind)
    {
        var vm = Vm;
        if (vm == null) return;
        Dispatcher.UIThread.Post(async () =>
        {
            var t = new Toast(title, message, kind);
            vm.Toasts.Add(t);
            while (vm.Toasts.Count > 4) vm.Toasts.RemoveAt(0);
            await Task.Delay(kind == ToastKind.Error ? 9000 : 4000);
            vm.Toasts.Remove(t);
        });
    }

    public static async Task CopyText(string text, string toast)
    {
        var cb = _window?.Clipboard;
        if (cb == null) return;
        await cb.SetTextAsync(text);
        Toast("Copied", toast, ToastKind.Info);
    }

    public static async Task<string?> PickOpenFileAsync(string title, string[] patterns, Window? owner = null)
    {
        var top = owner ?? (Window?)_window;
        if (top == null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType> { new("Supported files") { Patterns = patterns }, FilePickerFileTypes.All },
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> PickFolderAsync(string title, Window? owner = null)
    {
        var top = owner ?? (Window?)_window;
        if (top == null) return null;
        var dirs = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return dirs.FirstOrDefault()?.TryGetLocalPath();
    }

    public static async Task<string?> PickSaveFileAsync(string title, string suggested)
    {
        if (_window == null) return null;
        var f = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggested,
            DefaultExtension = "json",
            FileTypeChoices = new List<FilePickerFileType> { new("pm2 ecosystem (JSON)") { Patterns = new[] { "*.json" } } },
        });
        return f?.TryGetLocalPath();
    }

    // ---------------- menus ----------------

    private void Tray_Clicked(object? sender, EventArgs e)
    {
        if (_window is { IsVisible: true, IsActive: true }) HideMainWindow(); else ShowMainWindow();
    }
    private void Open_Click(object? sender, EventArgs e) => ShowMainWindow();
    private void RestartAll_Click(object? sender, EventArgs e) { ShowMainWindow(); Vm?.RestartAllCommand.Execute(null); }
    private void StopAll_Click(object? sender, EventArgs e) { ShowMainWindow(); Vm?.StopAllCommand.Execute(null); }
    private void Save_Click(object? sender, EventArgs e) => Vm?.SaveCommand.Execute(null);
    private void About_Click(object? sender, EventArgs e) { ShowMainWindow(); Vm?.OpenAboutCommand.Execute(null); }
    private void Settings_Click(object? sender, EventArgs e) { ShowMainWindow(); Vm?.OpenSettingsCommand.Execute(null); }
    private void Quit_Click(object? sender, EventArgs e) => Quit();
    private void Help_Click(object? sender, EventArgs e) => ShowHelp();
}
