using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Ampm2.Pm2;
using Ampm2.Sys;
using Ampm2.Ui;
using Microsoft.Win32;

namespace Ampm2;

public partial class AddProcessWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _nameTouched, _cwdTouched, _settingName;

    public AddProcessWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyChrome(this);
        NameBox.TextChanged += (_, _) => { if (!_settingName) _nameTouched = NameBox.Text.Length > 0; UpdatePreview(); };
        CwdBox.TextChanged += (_, _) => { if (CwdBox.IsKeyboardFocused) _cwdTouched = true; };
        ArgsBox.TextChanged += (_, _) => UpdatePreview();
        UpdateKind();
    }

    private string Kind => KindNpm.IsChecked == true ? "npm" : KindEco.IsChecked == true ? "eco" : "script";

    private void Kind_Checked(object sender, RoutedEventArgs e) { if (IsInitialized) UpdateKind(); }

    private void UpdateKind()
    {
        if (ScriptPanel == null) return;
        ScriptPanel.Visibility = Kind == "script" ? Visibility.Visible : Visibility.Collapsed;
        NpmPanel.Visibility = Kind == "npm" ? Visibility.Visible : Visibility.Collapsed;
        EcoPanel.Visibility = Kind == "eco" ? Visibility.Visible : Visibility.Collapsed;
        CommonPanel.Visibility = Kind == "eco" ? Visibility.Collapsed : Visibility.Visible;
        CwdPanel.Visibility = Kind == "npm" ? Visibility.Collapsed : Visibility.Visible;
        UpdatePreview();
    }

    // ---------- browsing ----------

    private void BrowseScript_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog
        {
            Title = "Choose the script or program to run",
            Filter = "Scripts and programs|*.js;*.mjs;*.cjs;*.ts;*.py;*.exe;*.cmd;*.bat|All files|*.*",
        };
        if (d.ShowDialog(this) == true) ScriptBox.Text = d.FileName;
    }

    private void BrowseProject_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFolderDialog { Title = "Choose the project folder" };
        if (d.ShowDialog(this) == true) { ProjectBox.Text = d.FolderName; LoadNpmScripts(); }
    }

    private void BrowseEco_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Title = "Choose an ecosystem file", Filter = "Ecosystem files|*.config.js;*.config.cjs;*.json;*.yml;*.yaml;*.js|All files|*.*" };
        if (d.ShowDialog(this) == true) EcoBox.Text = d.FileName;
    }

    private void BrowseCwd_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFolderDialog { Title = "Working directory" };
        if (Directory.Exists(CwdBox.Text)) d.InitialDirectory = CwdBox.Text;
        if (d.ShowDialog(this) == true) { CwdBox.Text = d.FolderName; _cwdTouched = true; }
    }

    private void Script_TextChanged(object sender, TextChangedEventArgs e)
    {
        var path = ScriptBox.Text.Trim().Trim('"');
        if (File.Exists(path))
        {
            if (!_cwdTouched) CwdBox.Text = Path.GetDirectoryName(path) ?? "";
            if (!_nameTouched) SetName(Path.GetFileNameWithoutExtension(path) is "index" or "server" or "main" or "app"
                ? new DirectoryInfo(Path.GetDirectoryName(path) ?? "app").Name : Path.GetFileNameWithoutExtension(path));
        }
        UpdatePreview();
    }

    private void SetName(string n) { _settingName = true; NameBox.Text = n; _settingName = false; }

    private void Project_LostFocus(object sender, RoutedEventArgs e) => LoadNpmScripts();

    private void LoadNpmScripts()
    {
        NpmScriptBox.Items.Clear();
        var pkg = Path.Combine(ProjectBox.Text.Trim().Trim('"'), "package.json");
        if (!File.Exists(pkg)) { NpmHint.Text = "No package.json found in that folder."; return; }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && !_nameTouched) SetName(n.GetString() ?? "");
            if (doc.RootElement.TryGetProperty("scripts", out var s) && s.ValueKind == JsonValueKind.Object)
                foreach (var p in s.EnumerateObject())
                    NpmScriptBox.Items.Add(new ComboBoxItem { Content = $"{p.Name}   ·   {p.Value}", Tag = p.Name });
            var pick = NpmScriptBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag is "start" or "serve" or "dev") ??
                       NpmScriptBox.Items.Cast<ComboBoxItem>().FirstOrDefault();
            NpmScriptBox.SelectedItem = pick;
            NpmHint.Text = "Runs node npm-cli.js run <script>, which works on Windows (pm2 cannot start npm.cmd directly).";
        }
        catch (Exception ex) { NpmHint.Text = "Could not read package.json: " + ex.Message; }
        UpdatePreview();
    }

    private void Interpreter_Changed(object sender, SelectionChangedEventArgs e) => UpdatePreview();
    private void Instances_Changed(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (PreviewText == null) return;
        PreviewText.Text = Kind switch
        {
            "eco" => "pm2 start " + Path.GetFileName(EcoBox.Text) + (OnlyBox.Text.Length > 0 ? " --only " + OnlyBox.Text : ""),
            "npm" => $"npm run {SelectedNpmScript() ?? "…"}  ·  {NameBox.Text}",
            _ => $"{InterpreterFor(ScriptBox.Text.Trim().Trim('"'))} {Path.GetFileName(ScriptBox.Text)} {ArgsBox.Text}".Trim(),
        };
    }

    private string? SelectedNpmScript() => (NpmScriptBox.SelectedItem as ComboBoxItem)?.Tag as string;

    private string InterpreterFor(string script)
    {
        var sel = (InterpreterBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "auto";
        if (sel != "auto") return sel;
        return Path.GetExtension(script).ToLowerInvariant() switch
        {
            ".js" or ".mjs" or ".cjs" or ".ts" or "" => "node",
            ".py" => "python",
            _ => "none",
        };
    }

    // ---------- start ----------

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        List<string> args;
        string display;
        try
        {
            if (Kind == "eco")
            {
                var f = EcoBox.Text.Trim().Trim('"');
                if (!File.Exists(f)) throw new InvalidOperationException("Choose an existing ecosystem file.");
                args = new List<string> { "start", f };
                if (OnlyBox.Text.Trim().Length > 0) { args.Add("--only"); args.Add(OnlyBox.Text.Trim()); }
                display = Path.GetFileName(f);
            }
            else
            {
                var cfg = BuildConfig(out display);
                Directory.CreateDirectory(Path.Combine(Settings.Dir, "apps"));
                var safe = string.Concat(display.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
                var file = Path.Combine(Settings.Dir, "apps", safe + ".json");
                File.WriteAllText(file, cfg, new UTF8Encoding(false));
                args = new List<string> { "start", file };
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
            if (added != null) { foreach (var i in _vm.Items) i.IsSelected = i == added; _vm.Selected = added; }
            Close();
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { StartButton.IsEnabled = true; StartText.Text = "Start"; }
    }

    private void ShowError(string msg) { ErrorText.Text = msg; ErrorPanel.Visibility = Visibility.Visible; }

    /// <summary>Builds a pm2 ecosystem JSON ({"apps":[...]}) with absolute paths.</summary>
    private string BuildConfig(out string name)
    {
        name = NameBox.Text.Trim();
        string script, cwd, interpreter, args = ArgsBox.Text.Trim();
        if (Kind == "npm")
        {
            cwd = ProjectBox.Text.Trim().Trim('"');
            if (!File.Exists(Path.Combine(cwd, "package.json"))) throw new InvalidOperationException("Choose a folder that contains package.json.");
            var s = SelectedNpmScript() ?? throw new InvalidOperationException("Choose an npm script.");
            script = NpmCli() ?? throw new InvalidOperationException("Could not find npm-cli.js next to node.exe. Is Node.js installed?");
            interpreter = "node";
            args = ("run " + s + (args.Length > 0 ? " -- " + args : "")).Trim();
            if (name.Length == 0) name = new DirectoryInfo(cwd).Name + ":" + s;
        }
        else
        {
            script = ScriptBox.Text.Trim().Trim('"');
            if (!File.Exists(script)) throw new InvalidOperationException("Choose an existing script or program.");
            interpreter = InterpreterFor(script);
            cwd = CwdBox.Text.Trim().Trim('"');
            if (cwd.Length == 0) cwd = Path.GetDirectoryName(script) ?? "";
            if (!Directory.Exists(cwd)) throw new InvalidOperationException("The working directory does not exist.");
            var ext = Path.GetExtension(script).ToLowerInvariant();
            if (interpreter == "none" && ext is ".cmd" or ".bat")
            {
                args = $"/d /c \"{script}\" {args}".Trim();
                script = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";
            }
            if (name.Length == 0) name = Path.GetFileNameWithoutExtension(script);
        }
        var finalName = name;
        if (_vm.Items.Any(i => i.Name.Equals(finalName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A process named '{finalName}' already exists. Pick another name.");

        var instTag = (InstancesBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "1";
        bool cluster = instTag != "1";
        if (cluster && interpreter != "node") throw new InvalidOperationException("Cluster mode only works for Node.js scripts.");

        var env = new List<KeyValuePair<string, string>>();
        foreach (var raw in EnvBox.Text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) throw new InvalidOperationException($"Environment line is not KEY=value: {line}");
            env.Add(new(line[..eq].Trim(), line[(eq + 1)..].Trim().Trim('"')));
        }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteStartArray("apps");
            w.WriteStartObject();
            w.WriteString("name", name);
            w.WriteString("script", script);
            w.WriteString("cwd", cwd);
            if (args.Length > 0) w.WriteString("args", args);
            w.WriteString("interpreter", interpreter);
            if (interpreter == "node" && NodeArgsBox.Text.Trim().Length > 0) w.WriteString("node_args", NodeArgsBox.Text.Trim());
            if (cluster)
            {
                w.WriteString("exec_mode", "cluster");
                if (instTag == "max") w.WriteString("instances", "max"); else w.WriteNumber("instances", int.Parse(instTag));
            }
            else w.WriteString("exec_mode", "fork");
            w.WriteBoolean("autorestart", AutoRestartBox.IsChecked == true);
            w.WriteBoolean("watch", WatchBox.IsChecked == true);
            if (WatchBox.IsChecked == true)
            {
                w.WriteStartArray("ignore_watch");
                w.WriteStringValue("node_modules"); w.WriteStringValue("logs"); w.WriteStringValue(".git");
                w.WriteEndArray();
            }
            if (MaxMemBox.Text.Trim().Length > 0) w.WriteString("max_memory_restart", MaxMemBox.Text.Trim());
            if (int.TryParse(DelayBox.Text.Trim(), out var delay) && delay > 0) w.WriteNumber("restart_delay", delay);
            w.WriteBoolean("time", TimeBox.IsChecked == true);
            w.WriteBoolean("windowsHide", true);
            if (env.Count > 0)
            {
                w.WriteStartObject("env");
                foreach (var kv in env) w.WriteString(kv.Key, kv.Value);
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string? NpmCli()
    {
        var node = Pm2Cli.NodePath;
        if (node == null) return null;
        var dir = Path.GetDirectoryName(node)!;
        var cli = Path.Combine(dir, "node_modules", "npm", "bin", "npm-cli.js");
        if (File.Exists(cli)) return cli;
        // nvm-style symlinked node folder: resolve the link target
        try
        {
            var target = new DirectoryInfo(dir).ResolveLinkTarget(true)?.FullName;
            if (target != null)
            {
                cli = Path.Combine(target, "node_modules", "npm", "bin", "npm-cli.js");
                if (File.Exists(cli)) return cli;
            }
        }
        catch { }
        return null;
    }
}
