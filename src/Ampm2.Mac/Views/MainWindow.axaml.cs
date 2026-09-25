using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Ampm2.Mac.ViewModels;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Ampm2.Mac.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow() : this(new MainViewModel(new MacSettings())) { }   // designer

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Width = vm.Settings.Width;
        Height = vm.Settings.Height;
        vm.LogsAppended += force => Dispatcher.UIThread.Post(() => ScrollLogs(force), DispatcherPriority.Background);
        vm.PropertyChanged += OnVmChanged;
        KeyDown += OnKeyDown;
        Activated += (_, _) => vm.SetVisible(true);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) vm.SetVisible(WindowState != WindowState.Minimized && IsVisible);
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        // destructive dialogs open with Cancel focused, so a stray Enter never confirms a delete
        if (e.PropertyName == nameof(MainViewModel.HasDialog) && _vm.HasDialog)
            Dispatcher.UIThread.Post(() => { if (_vm.Dialog?.Danger == true) DialogCancel.Focus(); else DialogOk.Focus(); }, DispatcherPriority.Input);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_vm.HasDialog) { _vm.CloseDialog(false); e.Handled = true; }
            else if (_vm.AboutOpen) { _vm.AboutOpen = false; e.Handled = true; }
            else if (_vm.SettingsOpen) { _vm.CloseSettingsCommand.Execute(null); e.Handled = true; }
        }
        else if (e.Key == Key.S && (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0 && DefinitionBox.IsFocused)
        {
            if (_vm.SaveDefinitionCommand.CanExecute(null)) _vm.SaveDefinitionCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (WindowState == WindowState.Normal) { _vm.Settings.Width = Width; _vm.Settings.Height = Height; }
        _vm.Settings.Save();
        if (!App.Quitting && _vm.Settings.CloseToMenuBar)
        {
            e.Cancel = true;
            App.HideMainWindow();
            return;
        }
        if (!App.Quitting) { e.Cancel = true; App.Quit(); }
        base.OnClosing(e);
    }

    private void ScrollLogs(bool force)
    {
        if (_vm.PauseLogs || _vm.Logs.Count == 0) return;
        var sv = LogList.Scroll as ScrollViewer;
        if (!force && sv != null && sv.Offset.Y < sv.Extent.Height - sv.Viewport.Height - 40) return;   // user scrolled up: don't jump
        LogList.ScrollIntoView(_vm.Logs.Count - 1);
    }

    private async void LogList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && (e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0 && LogList.SelectedItems is { Count: > 0 } sel)
        {
            var sb = new StringBuilder();
            foreach (var l in sel.OfType<LogLine>().OrderBy(l => _vm.Logs.IndexOf(l))) sb.AppendLine(l.Text);
            if (Clipboard != null) await Clipboard.SetTextAsync(sb.ToString());
            e.Handled = true;
        }
    }

    private void ProcessList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm.Selected != null) _vm.SetTabCommand.Execute(_vm.LogsVisible ? "overview" : "logs");
    }

    private void Overlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: "about" }) _vm.AboutOpen = false;
        else if (sender is Control { Tag: "settings" }) _vm.CloseSettingsCommand.Execute(null);
    }

    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;
}
