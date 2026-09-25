using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ampm2.Pm2;

namespace Ampm2.Sys;

/// <summary>What the "New process" form collects; <see cref="Build"/> turns it into a pm2 ecosystem app.</summary>
public sealed class NewAppSpec
{
    public string Kind { get; set; } = "script";        // script | npm
    public string Script { get; set; } = "";
    public string Interpreter { get; set; } = "auto";   // auto | node | python | bun | none
    public string NodeArgs { get; set; } = "";
    public string ProjectDir { get; set; } = "";         // npm
    public string NpmScript { get; set; } = "";          // npm
    public string Name { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string Args { get; set; } = "";
    public string Instances { get; set; } = "1";         // 1 | 2 | 4 | max
    public string MaxMemory { get; set; } = "";
    public string RestartDelay { get; set; } = "";
    public string Env { get; set; } = "";                 // KEY=value lines
    public bool AutoRestart { get; set; } = true;
    public bool Watch { get; set; }
    public bool Timestamps { get; set; } = true;

    public static string InterpreterFor(string script, string choice)
    {
        if (choice != "auto") return choice;
        var ext = Path.GetExtension(script).ToLowerInvariant();
        if (ext == "") return OperatingSystem.IsWindows() ? "node" : "none";   // no extension on macOS/Linux = an executable
        return ext switch
        {
            ".js" or ".mjs" or ".cjs" or ".ts" => "node",
            ".py" => OperatingSystem.IsWindows() ? "python" : "python3",
            _ => "none",
        };
    }

    /// <summary>Validates and builds the app definition (absolute paths). Throws InvalidOperationException with a user-facing message.</summary>
    public JsonObject Build(Func<string, bool> nameTaken)
    {
        string name = Name.Trim(), script, cwd, interpreter, args = Args.Trim();
        if (Kind == "npm")
        {
            cwd = ProjectDir.Trim().Trim('"');
            if (!File.Exists(Path.Combine(cwd, "package.json"))) throw new InvalidOperationException("Choose a folder that contains package.json.");
            if (NpmScript.Length == 0) throw new InvalidOperationException("Choose an npm script.");
            args = ("run " + NpmScript + (args.Length > 0 ? " -- " + args : "")).Trim();
            if (OperatingSystem.IsWindows())
            {
                // pm2 cannot start npm.cmd on Windows: run npm-cli.js with node instead
                script = NpmCliJs() ?? throw new InvalidOperationException("Could not find npm-cli.js next to node.exe. Is Node.js installed?");
                interpreter = "node";
            }
            else
            {
                script = Pm2Cli.NpmPath ?? throw new InvalidOperationException("npm was not found. Is Node.js installed?");
                interpreter = "none";   // npm is an executable script with a shebang
            }
            if (name.Length == 0) name = new DirectoryInfo(cwd).Name + ":" + NpmScript;
        }
        else
        {
            script = Script.Trim().Trim('"');
            if (!File.Exists(script)) throw new InvalidOperationException("Choose an existing script or program.");
            interpreter = InterpreterFor(script, Interpreter);
            cwd = Cwd.Trim().Trim('"');
            if (cwd.Length == 0) cwd = Path.GetDirectoryName(script) ?? "";
            if (!Directory.Exists(cwd)) throw new InvalidOperationException("The working directory does not exist.");
            if (OperatingSystem.IsWindows() && interpreter == "none" && Path.GetExtension(script).ToLowerInvariant() is ".cmd" or ".bat")
            {
                args = $"/d /c \"{script}\" {args}".Trim();
                script = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe";
            }
            if (name.Length == 0) name = Path.GetFileNameWithoutExtension(script);
        }
        if (nameTaken(name)) throw new InvalidOperationException($"A process named '{name}' already exists. Pick another name.");
        bool cluster = Instances != "1";
        if (cluster && interpreter != "node") throw new InvalidOperationException("Cluster mode only works for Node.js scripts.");

        var o = new JsonObject { ["name"] = name, ["script"] = script, ["cwd"] = cwd };
        if (args.Length > 0) o["args"] = args;
        o["interpreter"] = interpreter;
        if (interpreter == "node" && NodeArgs.Trim().Length > 0) o["node_args"] = NodeArgs.Trim();
        o["exec_mode"] = cluster ? "cluster" : "fork";
        if (cluster) o["instances"] = Instances == "max" ? "max" : int.Parse(Instances);
        o["autorestart"] = AutoRestart;
        o["watch"] = Watch;
        if (Watch) o["ignore_watch"] = new JsonArray("node_modules", "logs", ".git");
        if (MaxMemory.Trim().Length > 0) o["max_memory_restart"] = MaxMemory.Trim();
        if (int.TryParse(RestartDelay.Trim(), out var d) && d > 0) o["restart_delay"] = d;
        o["time"] = Timestamps;
        if (OperatingSystem.IsWindows()) o["windowsHide"] = true;

        var env = new JsonObject();
        foreach (var raw in Env.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) throw new InvalidOperationException($"Environment line is not KEY=value: {line}");
            env[line[..eq].Trim()] = line[(eq + 1)..].Trim().Trim('"');
        }
        if (env.Count > 0) o["env"] = env;
        return o;
    }

    /// <summary>Writes the app as an ecosystem file in DataPaths.Dir/apps (so the Saved list keeps its exact env) and returns the path.</summary>
    public static string WriteConfig(JsonObject app)
    {
        var name = AppLibrary.Name(app)!;
        var dir = Path.Combine(DataPaths.Dir, "apps");
        Directory.CreateDirectory(dir);
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
        var file = Path.Combine(dir, safe + ".json");
        File.WriteAllText(file, AppLibrary.Ecosystem(new[] { app }), new System.Text.UTF8Encoding(false));
        return file;
    }

    /// <summary>npm scripts listed in a project's package.json (name, command), plus the package name.</summary>
    public static (string? PackageName, List<(string Name, string Command)> Scripts) ReadPackageJson(string dir)
    {
        var list = new List<(string, string)>();
        var pkg = Path.Combine(dir, "package.json");
        if (!File.Exists(pkg)) return (null, list);
        var root = JsonNode.Parse(File.ReadAllText(pkg), documentOptions: new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
        if (root?["scripts"] is JsonObject s)
            foreach (var kv in s) list.Add((kv.Key, kv.Value?.ToString() ?? ""));
        return (root?["name"]?.ToString(), list);
    }

    private static string? NpmCliJs()
    {
        var node = Pm2Cli.NodePath;
        if (node == null) return null;
        var dir = Path.GetDirectoryName(node)!;
        var cli = Path.Combine(dir, "node_modules", "npm", "bin", "npm-cli.js");
        if (File.Exists(cli)) return cli;
        try
        {
            var target = new DirectoryInfo(dir).ResolveLinkTarget(true)?.FullName;
            if (target != null && File.Exists(cli = Path.Combine(target, "node_modules", "npm", "bin", "npm-cli.js"))) return cli;
        }
        catch { }
        return null;
    }
}
