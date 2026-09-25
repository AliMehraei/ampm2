using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ampm2.Mac.ViewModels;
using Ampm2.Pm2;
using Ampm2.Sys;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Ampm2.Mac.Views;

public partial class AddProcessWindow : Window
{
    private readonly MainViewModel _vm;
    private string _kind = "script";
    private bool _nameTouched, _cwdTouched, _settingName;

    public AddProcessWindow() : this(App.Vm!) { }   // designer

    public AddProcessWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        CwdBox.GotFocus += (_, _) => _cwdTouched = true;
    }

    private void Kind_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string k }) return;
        _kind = k;
        KindScript.IsChecked = k == "script"; KindNpm.IsChecked = k == "npm"; KindEco.IsChecked = k == "eco";
        ScriptPanel.IsVisible = k == "script";
        NpmPanel.IsVisible = k == "npm";
        EcoPanel.IsVisible = k == "eco";
        CommonPanel.IsVisible = k != "eco";
        CwdPanel.IsVisible = k != "npm";
    }

    private async void BrowseScript_Click(object? sender, RoutedEventArgs e)
    {
        var f = await App.PickOpenFileAsync("Choose the script or program to run", new[] { "*" }, this);
        if (f != null) ScriptBox.Text = f;
    }

    private async void BrowseProject_Click(object? sender, RoutedEventArgs e)
    {
        var d = await App.PickFolderAsync("Choose the project folder", this);
        if (d != null) { ProjectBox.Text = d; LoadNpmScripts(); }
    }

    private async void BrowseEco_Click(object? sender, RoutedEventArgs e)
    {
        var f = await App.PickOpenFileAsync("Choose an ecosystem file", new[] { "*.js", "*.cjs", "*.json", "*.yml", "*.yaml" }, this);
        if (f != null) EcoBox.Text = f;
    }

    private async void BrowseCwd_Click(object? sender, RoutedEventArgs e)
    {
        var d = await App.PickFolderAsync("Working directory", this);
        if (d != null) { CwdBox.Text = d; _cwdTouched = true; }
    }

    private void Name_Changed(object? sender, TextChangedEventArgs e) { if (!_settingName) _nameTouched = (NameBox.Text ?? "").Length > 0; }

    private void Script_Changed(object? sender, TextChangedEventArgs e)
    {
        var path = (ScriptBox.Text ?? "").Trim().Trim('"');
        if (!File.Exists(path)) return;
        if (!_cwdTouched) CwdBox.Text = Path.GetDirectoryName(path) ?? "";
        if (!_nameTouched)
        {
            var n = Path.GetFileNameWithoutExtension(path);
            SetName(n is "index" or "server" or "main" or "app" ? new DirectoryInfo(Path.GetDirectoryName(path) ?? "app").Name : n);
        }
        PreviewText.Text = $"{NewAppSpec.InterpreterFor(path, SelTag(InterpreterBox) ?? "auto")} {Path.GetFileName(path)}";
    }

    private void SetName(string n) { _settingName = true; NameBox.Text = n; _settingName = false; }

    private void Project_LostFocus(object? sender, RoutedEventArgs e) => LoadNpmScripts();

    private void LoadNpmScripts()
    {
        NpmScriptBox.Items.Clear();
        try
        {
            var (pkgName, scripts) = NewAppSpec.ReadPackageJson((ProjectBox.Text ?? "").Trim());
            if (pkgName == null && scripts.Count == 0) { NpmHint.Text = "No package.json found in that folder."; return; }
            if (pkgName != null && !_nameTouched) SetName(pkgName);
            foreach (var (n, cmd) in scripts) NpmScriptBox.Items.Add(new ComboBoxItem { Content = $"{n}   ·   {cmd}", Tag = n });
            var items = NpmScriptBox.Items.OfType<ComboBoxItem>().ToList();
            NpmScriptBox.SelectedItem = items.FirstOrDefault(i => (string)i.Tag! is "start" or "serve" or "dev") ?? items.FirstOrDefault();
            NpmHint.Text = "Runs npm run <script> in the project folder.";
        }
        catch (Exception ex) { NpmHint.Text = "Could not read package.json: " + ex.Message; }
    }

    private static string? SelTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private async void Start_Click(object? sender, RoutedEventArgs e)
    {
        ErrorPanel.IsVisible = false;
        List<string> args;
        string display;
        try
        {
            if (_kind == "eco")
            {
                var f = (EcoBox.Text ?? "").Trim().Trim('"');
                if (!File.Exists(f)) throw new InvalidOperationException("Choose an existing ecosystem file.");
                args = new List<string> { "start", f };
                var only = (OnlyBox.Text ?? "").Trim();
                if (only.Length > 0) { args.Add("--only"); args.Add(only); }
                display = Path.GetFileName(f);
            }
            else
            {
                var spec = new NewAppSpec
                {
                    Kind = _kind, Script = ScriptBox.Text ?? "", Interpreter = SelTag(InterpreterBox) ?? "auto", NodeArgs = NodeArgsBox.Text ?? "",
                    ProjectDir = ProjectBox.Text ?? "", NpmScript = SelTag(NpmScriptBox) ?? "", Name = NameBox.Text ?? "", Cwd = CwdBox.Text ?? "",
                    Args = ArgsBox.Text ?? "", Instances = SelTag(InstancesBox) ?? "1", MaxMemory = MaxMemBox.Text ?? "", RestartDelay = DelayBox.Text ?? "",
                    Env = EnvBox.Text ?? "", AutoRestart = AutoRestartBox.IsChecked == true, Watch = WatchBox.IsChecked == true, Timestamps = TimeBox.IsChecked == true,
                };
                var app = spec.Build(n => _vm.Items.Any(i => i.Name.Equals(n, StringComparison.OrdinalIgnoreCase)));
                display = AppLibrary.Name(app)!;
                args = new List<string> { "start", NewAppSpec.WriteConfig(app) };
            }
        }
        catch (Exception ex) { ShowError(ex.Message); return; }

        StartButton.IsEnabled = false;
        StartText.Text = "Starting…";
        try
        {
            var r = await Pm2Cli.Pm2Async(args);
            if (!r.Ok || r.Output.Contains("[PM2][ERROR]", StringComparison.OrdinalIgnoreCase))
            {
                ShowError(string.Join("\n", r.Output.Split('\n').Where(l => l.Trim().Length > 0).TakeLast(8)));
                return;
            }
            App.Toast("Started", display, ToastKind.Success);
            if (SaveBox.IsChecked == true) await Pm2Cli.Pm2Async(new[] { "save" });
            await _vm.RefreshListAsync();
            var added = _vm.Items.FirstOrDefault(i => i.Name == display);
            if (added != null) _vm.Selected = added;
            Close();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { StartButton.IsEnabled = true; StartText.Text = "Start"; }
    }

    private void ShowError(string msg) { ErrorText.Text = msg; ErrorPanel.IsVisible = true; }
}
