using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Ampm2.Sys;
using Ampm2.Ui;

namespace Ampm2;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _loadingSettings;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Icon = Logo.Image();
        LogoImage.Source = Logo.Image();
        AboutLogo.Source = Logo.Image();
        Width = vm.Settings.Width;
        Height = vm.Settings.Height;
        DetailColumn.Width = new GridLength(Math.Clamp(vm.Settings.DetailWidth, 320, 900));

        SourceInitialized += (_, _) => ThemeManager.ApplyChrome(this);
        vm.LogsAppended += force => { if (force) _forceScroll = true; ScrollLogsToEnd(); };
        vm.RequestTab += tab => { if (tab == "logs") TabLogs.IsChecked = true; else TabOverview.IsChecked = true; };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SettingsOpen) && vm.SettingsOpen) LoadSettingsUi();
            if (e.PropertyName == nameof(MainViewModel.ViewMode)) (vm.IsSavedView ? ViewSaved : ViewProcesses).IsChecked = true;
            if (e.PropertyName == nameof(MainViewModel.HasDialog) && vm.HasDialog)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
                    () => { if (vm.Dialog?.Danger == true) DialogCancel.Focus(); else DialogOk.Focus(); });
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) { vm.SetVisible(false); Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, Native.TrimSelf); }
            else vm.SetVisible(IsVisible);
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm.HasDialog)
        {
            // Escape cancels. Enter/Space press only the FOCUSED dialog button, and destructive dialogs
            // open with Cancel focused, so a stray Enter typed while ampm2 grabs focus never confirms a delete.
            if (e.Key == Key.Escape) { _vm.CloseDialog(false); e.Handled = true; }
            else if (e.Key is Key.Enter or Key.Space)
            {
                if (Keyboard.FocusedElement == DialogOk) _vm.CloseDialog(true);
                else if (Keyboard.FocusedElement == DialogCancel) _vm.CloseDialog(false);
                e.Handled = true;
            }
            else if (e.Key == Key.Tab) { (DialogOk.IsKeyboardFocused ? (UIElement)DialogCancel : DialogOk).Focus(); e.Handled = true; }
            return;
        }
        if (_vm.AboutOpen && e.Key == Key.Escape) { _vm.AboutOpen = false; e.Handled = true; return; }
        if (_vm.SettingsOpen && e.Key == Key.Escape) { _vm.CloseSettingsCommand.Execute(null); e.Handled = true; return; }
        if (DefinitionBox.IsKeyboardFocused && e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_vm.SaveDefinitionCommand.CanExecute(null)) _vm.SaveDefinitionCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocused) { _vm.SearchText = ""; ProcessList.Focus(); e.Handled = true; }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveBounds();
        if (!App.Exiting && _vm.Settings.CloseToTray)
        {
            e.Cancel = true;
            App.HideMainWindow();
            return;
        }
        if (!App.Exiting)
        {
            e.Cancel = true;
            App.ExitApp();
        }
        base.OnClosing(e);
    }

    private void SaveBounds()
    {
        if (WindowState == WindowState.Normal) { _vm.Settings.Width = ActualWidth; _vm.Settings.Height = ActualHeight; }
        _vm.Settings.DetailWidth = DetailColumn.ActualWidth;
        _vm.Settings.Save();
    }

    // ---------- list ----------

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string t }) _vm.StatusFilter = t;
    }

    private void ProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e) => _vm.UpdateSelectionCount();

    private void View_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string t } && IsInitialized) _vm.ViewMode = t;
    }

    private void SavedList_SelectionChanged(object sender, SelectionChangedEventArgs e) => _vm.UpdateSavedSelectionCount();

    private void SavedList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && _vm.RemoveSavedCommand.CanExecute(null)) { _vm.RemoveSavedCommand.Execute(null); e.Handled = true; }
    }

    private void ProcessList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && _vm.DeleteCommand.CanExecute(null)) { _vm.DeleteCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Enter && _vm.Selected != null) { TabLogs.IsChecked = true; e.Handled = true; }
    }

    private void ProcessList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindParent<ButtonBase>(d) != null) return;
        if (_vm.Selected != null) TabLogs.IsChecked = !TabLogs.IsChecked;
        if (TabLogs.IsChecked != true) TabOverview.IsChecked = true;
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        var cm = MoreButton.ContextMenu;
        cm.DataContext = _vm;
        cm.PlacementTarget = MoreButton;
        cm.Placement = PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    private void Splitter_DragCompleted(object sender, DragCompletedEventArgs e) => _vm.Settings.DetailWidth = DetailColumn.ActualWidth;

    // ---------- detail / logs ----------

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string t }) _vm.DetailTab = t;
    }

    private void Stream_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string t }) _vm.LogStream = t;
    }

    private void Pause_Changed(object sender, RoutedEventArgs e)
    {
        _vm.PauseLogs = PauseToggle.IsChecked == true;
        if (!_vm.PauseLogs) ScrollLogsToEnd();
    }

    private ScrollViewer? _logScroll;
    private void ScrollLogsToEnd()
    {
        if (_vm.PauseLogs || LogList.Items.Count == 0) return;
        _logScroll ??= FindChild<ScrollViewer>(LogList);
        if (_logScroll != null)
        {
            // Follow only while the user is at (or near) the bottom.
            bool atBottom = _logScroll.VerticalOffset >= _logScroll.ScrollableHeight - 40 || _logScroll.ScrollableHeight == 0;
            if (!atBottom && !_forceScroll) return;
            _forceScroll = false;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => _logScroll.ScrollToEnd());
        }
        else LogList.ScrollIntoView(LogList.Items[^1]);
    }
    private bool _forceScroll = true;

    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) return t;
            var r = FindChild<T>(c);
            if (r != null) return r;
        }
        return null;
    }

    private void LogList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var sb = new StringBuilder();
            foreach (var l in LogList.SelectedItems.Cast<LogLine>().OrderBy(l => LogList.Items.IndexOf(l))) sb.AppendLine(l.Text);
            try { Clipboard.SetText(sb.ToString()); } catch { }
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { LogList.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.End) { _forceScroll = true; ScrollLogsToEnd(); }
    }

    // ---------- dialog ----------

    private void DialogOk_Click(object sender, RoutedEventArgs e) => _vm.CloseDialog(true);
    private void DialogCancel_Click(object sender, RoutedEventArgs e) => _vm.CloseDialog(false);

    // ---------- settings ----------

    private void LoadSettingsUi()
    {
        _loadingSettings = true;
        var s = _vm.Settings;
        (s.Theme switch { "Dark" => ThemeDark, "Light" => ThemeLight, _ => ThemeSystem }).IsChecked = true;
        Select(MetricsBox, s.MetricsIntervalSec.ToString(), 1);
        Select(ListIntervalBox, s.ListIntervalSec.ToString(), 1);
        ElevatedTaskSwitch.IsChecked = false;
        _ = RefreshTaskSwitch();
        _loadingSettings = false;
    }

    private async System.Threading.Tasks.Task RefreshTaskSwitch() => ElevatedTaskSwitch.IsChecked = await Elevation.ElevatedTaskIsMineAsync();

    private static void Select(ComboBox box, string tag, int fallback)
    {
        foreach (ComboBoxItem i in box.Items) if ((string)i.Tag == tag) { box.SelectedItem = i; return; }
        box.SelectedIndex = fallback;
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || sender is not RadioButton { Tag: string t }) return;
        _vm.Settings.Theme = t;
        ThemeManager.Apply(t);
    }

    private void Interval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || MetricsBox == null || ListIntervalBox == null) return;
        if (MetricsBox.SelectedItem is ComboBoxItem { Tag: string m }) _vm.Settings.MetricsIntervalSec = int.Parse(m);
        if (ListIntervalBox.SelectedItem is ComboBoxItem { Tag: string l }) _vm.Settings.ListIntervalSec = int.Parse(l);
        _vm.ApplyIntervals();
    }

    private async void ElevatedTask_Click(object sender, RoutedEventArgs e)
    {
        bool want = ElevatedTaskSwitch.IsChecked == true;
        var r = want ? await Elevation.RegisterElevatedTaskAsync() : await Elevation.UnregisterElevatedTaskAsync();
        if (!r.Ok) App.Toast("Could not update the task", r.Output, ToastKind.Error);
        else App.Toast(want ? "Elevated launch enabled" : "Elevated launch removed",
            want ? "Starting ampm2 normally now opens it as administrator without a prompt." : "ampm2 starts non-elevated again.", ToastKind.Success);
        await RefreshTaskSwitch();
    }

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e) => _vm.CloseSettingsCommand.Execute(null);
    private void AboutOverlay_MouseDown(object sender, MouseButtonEventArgs e) => _vm.AboutOpen = false;
    private void Card_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
