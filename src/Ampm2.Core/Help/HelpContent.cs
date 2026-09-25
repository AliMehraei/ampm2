using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ampm2.Help;

public enum HelpKind { Heading, Paragraph, Bullet, Step, Key, Note, Code }

[Flags]
public enum HelpPlatform { Windows = 1, Mac = 2, All = Windows | Mac }

/// <summary>One block of a help topic. Term is the bold lead-in of a bullet, or the key combination of a shortcut.</summary>
public sealed class HelpBlock
{
    public HelpBlock(HelpKind kind, string text, string term = "", HelpPlatform platform = HelpPlatform.All)
    { Kind = kind; Text = text; Term = term; Platform = platform; }
    public HelpKind Kind { get; }
    public string Text { get; }
    public string Term { get; }
    public HelpPlatform Platform { get; }
    public int Number { get; internal set; }       // steps: 1, 2, 3 … within a topic
    public bool IsHeading => Kind == HelpKind.Heading;
    public bool IsParagraph => Kind == HelpKind.Paragraph;
    public bool IsBullet => Kind == HelpKind.Bullet;
    public bool IsStep => Kind == HelpKind.Step;
    public bool IsKey => Kind == HelpKind.Key;
    public bool IsNote => Kind == HelpKind.Note;
    public bool IsCode => Kind == HelpKind.Code;
    public bool HasTerm => Term.Length > 0;
    public string NumberText => Number + ".";
    /// <summary>Text for the app (Markdown code ticks removed).</summary>
    public string DisplayText => Text.Replace("`", "");
}

public sealed class HelpTopic
{
    public HelpTopic(string id, string title, string summary, IReadOnlyList<HelpBlock> blocks)
    { Id = id; Title = title; Summary = summary; Blocks = blocks; }
    public string Id { get; }
    public string Title { get; }
    public string Summary { get; }
    public IReadOnlyList<HelpBlock> Blocks { get; }

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var q = query.Trim();
        return Title.Contains(q, StringComparison.OrdinalIgnoreCase) || Summary.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               Blocks.Any(b => b.Text.Contains(q, StringComparison.OrdinalIgnoreCase) || b.Term.Contains(q, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// The in-app help guide, shared by the Windows and macOS apps (and exported to docs/HELP.md).
/// Blocks marked Win/Mac appear only on that platform.
/// </summary>
public static class HelpContent
{
    private const HelpPlatform W = HelpPlatform.Windows, M = HelpPlatform.Mac;
    private static HelpBlock H(string t, HelpPlatform p = HelpPlatform.All) => new(HelpKind.Heading, t, "", p);
    private static HelpBlock P(string t, HelpPlatform p = HelpPlatform.All) => new(HelpKind.Paragraph, t, "", p);
    private static HelpBlock B(string term, string t, HelpPlatform p = HelpPlatform.All) => new(HelpKind.Bullet, t, term, p);
    private static HelpBlock S(string t, HelpPlatform p = HelpPlatform.All) => new(HelpKind.Step, t, "", p);
    private static HelpBlock K(string keys, string t, HelpPlatform p = HelpPlatform.All) => new(HelpKind.Key, t, keys, p);
    private static HelpBlock N(string t, HelpPlatform p = HelpPlatform.All) => new(HelpKind.Note, t, "", p);
    private static HelpBlock C(string t, HelpPlatform p = HelpPlatform.All) => new(HelpKind.Code, t, "", p);

    private static readonly List<(string Id, string Title, string Summary, HelpBlock[] Blocks)> All = new()
    {
        ("start", "Getting started", "What ampm2 is and how to begin.", new[]
        {
            P("ampm2 is a desktop manager for pm2, the Node.js process manager. It shows every process pm2 runs, with live status, CPU, memory, uptime and restarts. You can start, stop, restart and delete processes, read their logs, and add new ones, without typing pm2 commands."),
            P("ampm2 talks to the pm2 daemon directly over pm2's own connection, so it stays light: it never runs `pm2 list` in the background, and it uses almost no CPU while its window is hidden."),
            H("First launch"),
            S("Open ampm2. It finds the pm2 daemon on its own and shows its processes."),
            S("If pm2 is not installed, ampm2 offers to install Node.js and pm2 for you. See \"Installing Node.js and pm2\"."),
            S("If the pm2 daemon is not running, click Start pm2. See \"After a reboot\" to bring your processes back."),
            S("If pm2 runs as administrator, ampm2 asks to relaunch elevated. See \"pm2 running as administrator\".", W),
            S("Select a process to see its details and logs on the right."),
            H("The window"),
            B("Header", "The connection status, the counters for online, stopped and errored processes, total CPU and memory, and the main buttons."),
            B("Above the list", "Start all, Restart all and Stop all, and Refresh."),
            B("Process list", "Everything pm2 runs. Switch to the Saved list with the tabs above it."),
            B("Details pane", "Overview and logs of the selected process, or the definition of the selected saved app."),
            B("Tray icon", "ampm2 keeps running in the notification area when you close the window.", W),
            B("Menu bar icon", "ampm2 keeps running in the menu bar when you close the window.", M),
        }),
        ("list", "The process list", "Columns, status colours, search, filters and sorting.", new[]
        {
            H("Columns"),
            B("Name", "The pm2 app name, with badges for cluster mode (\"cluster ×2\") and a namespace other than default. The line below shows the script path."),
            B("Status", "online, stopped, errored, launching or waiting (restart pending)."),
            B("CPU", "Percent of one core, like pm2 and top: 200% means two full cores. It covers the app's whole process tree, because on Windows pm2 often runs the real server as a child of a cmd or bash wrapper."),
            B("Memory", "Memory used by the app's whole process tree. Hover it to see how many processes the tree has."),
            B("Uptime", "Time since the process last started."),
            B("↻ (restarts)", "How often pm2 restarted it. Amber when it is above zero. Reset it with Reset restart counter.", W),
            B("↻ (restarts)", "How often pm2 restarted it. Amber when it is above zero.", M),
            B("ID", "pm2's id. Each cluster instance has its own id, so a cluster app appears once per instance."),
            H("Status colours"),
            B("Green", "online."),
            B("Grey", "stopped."),
            B("Red", "errored: pm2 gave up after too many quick crashes. Open its logs to see why."),
            B("Amber", "starting, or waiting to restart."),
            H("Finding processes"),
            B("Search", "Type part of a name, an id or a script path. Press Ctrl+F to jump to the search box and Esc to clear it.", W),
            B("Search", "Type part of a name, an id or a script path.", M),
            B("Filters", "All, Online, Stopped or Errored."),
            B("Sort", "Click a column header to sort by it. Click it again to reverse the order. CPU and memory sort highest first."),
            N("Status changes appear instantly, because ampm2 listens to pm2's event bus. CPU and memory are sampled every 2 seconds while the window is visible; change that in Settings."),
        }),
        ("actions", "Starting, stopping and restarting", "Controlling processes, one at a time or together.", new[]
        {
            B("Start", "Starts a stopped or errored process."),
            B("Stop", "Stops (pauses) a process. pm2 keeps it in its list, so you can start it again."),
            B("Restart", "Stops and starts it again. The restart counter goes up by one."),
            B("Reload", "A graceful reload. In cluster mode pm2 replaces instances one by one, with no downtime. In fork mode it behaves like a restart."),
            B("Start / Stop", "One button that changes: it shows Stop while the process runs, and Start when it is stopped."),
            B("Delete", "Stops the process and removes it from pm2. Its script and log files stay on disk, and it stays in the Saved list so you can start it again later."),
            H("Where to find them"),
            B("Row buttons", "Hover a row, or select it, to show Start or Stop, Restart, Logs and Delete."),
            B("Right-click a row", "Start, Stop, Restart, Reload, Logs, Open working folder, Reset restart counter, Flush logs and Delete.", W),
            B("Right-click a row", "Start, Stop, Restart, Reload, Logs, Show working folder, Flush logs and Delete.", M),
            B("Details pane", "Start or Stop, Restart, Reload and Delete for the selected process."),
            B("Above the list", "Start all starts every stopped or errored process, Restart all restarts every process, and Stop all stops every running one. Next to them, Refresh reads the list again."),
            B("Header", "Save the process list (pm2 save)."),
            B("Tray menu", "Restart all and Stop all are also in the tray icon's menu.", W),
            B("Menu bar", "Restart all and Stop all are also in the menu bar icon's menu.", M),
            H("Several processes at once", W),
            P("Select several rows with Ctrl+click or Shift+click. A bar appears below the list with Start, Stop, Restart and Delete for all of them.", W),
            H("Safety"),
            P("Delete, Stop all and Flush logs ask for confirmation. The confirmation opens with Cancel selected, so pressing Enter by accident never deletes anything. Turn confirmations off in Settings if you prefer."),
        }),
        ("details", "Details and live logs", "The Overview and Logs tabs.", new[]
        {
            H("Overview"),
            P("The CPU and memory graphs cover the last 60 samples. Below them are the script, working folder, arguments, interpreter, start and creation times, restarts, process tree, auto-restart, watch, memory limit, versions, log file paths and the user pm2 runs as. You can select and copy any value."),
            B("Log paths", "The folder button next to a log path shows the file in Explorer.", W),
            B("Log paths", "The folder button in the Logs tab shows the log file in Finder.", M),
            H("Logs"),
            P("The Logs tab first shows the end of the stdout and stderr files, then a \"live\" marker, then new lines as the app writes them. stderr lines are red."),
            B("all · out · err", "Show both streams, or only one."),
            B("Pause", "Freezes the view. New lines are kept and appear when you resume."),
            B("Reload", "Reads the log files again."),
            B("Clear", "Empties the view. The log files are not touched."),
            B("Open log file", "Opens the stdout log in your default editor.", W),
            B("Follow", "The view follows new lines while you are at the bottom. Scroll up to read, and it stays put."),
            B("Flush logs", "Empties the log files themselves (pm2 flush). Right-click a process to flush its logs.", M),
            B("Flush logs", "Empties the log files themselves (pm2 flush). Right-click a process to flush its logs, or use ⋯ ▸ Flush all logs.", W),
            H("Keyboard"),
            K("Ctrl+C", "Copy the selected log lines.", W),
            K("Ctrl+A", "Select all log lines.", W),
            K("End", "Jump to the newest line.", W),
            K("⌘C", "Copy the selected log lines.", M),
        }),
        ("saved", "The Saved list", "ampm2's own copy of your process list.", new[]
        {
            P("pm2 keeps its list in dump.pm2, but that file is easy to lose: pm2 does not start with the computer, and running `pm2 save` after `pm2 kill` saves an empty list. The Saved list is ampm2's own copy of every app definition. It survives all of that, and it can bring apps back that pm2 has forgotten."),
            H("How it stays up to date"),
            B("Keep in sync with pm2", "On by default. Every app pm2 runs is copied into the Saved list when it appears or changes."),
            B("Never removes", "When an app disappears from pm2, it stays in the Saved list and shows \"not in pm2\", with a Start button."),
            B("Save from pm2", "Copies the current pm2 list right away, even with sync off."),
            H("Getting apps back"),
            B("Start missing", "Starts every saved app pm2 does not have, then saves pm2's list."),
            B("Start in pm2", "Starts one saved app. Use the row button, the right-click menu or the details pane."),
            H("Import and export"),
            B("Import…", "Reads an ecosystem .json, .config.js or .cjs file (evaluated with Node.js), or pm2's own dump.pm2. Relative paths are resolved against the file, as pm2 does. If a name already exists, ampm2 asks before replacing it."),
            B("Export all…", "Writes every saved app to an ecosystem.json. Start it anywhere with `pm2 start ecosystem.json`."),
            B("Export…", "On a row or in the details pane, exports just that app."),
            H("Editing a definition"),
            P("Select a saved app to see its definition as pm2 ecosystem JSON. Edit it and click Save changes to change how it starts next time; Revert undoes your edits. The name and script fields are required. Changes apply the next time you start the app from the Saved list."),
            K("Ctrl+S", "Save the definition while editing it.", W),
            K("⌘S", "Save the definition while editing it.", M),
            H("Environment variables"),
            P("pm2 copies the whole environment into every app: PATH, your user folders, and any tokens exported in the terminal that started pm2. The Saved list keeps only the app's own variables. It drops anything that equals your system environment and known shell or terminal variables. Apps you created with New process keep their exact original settings."),
            H("Removing"),
            P("Remove from Saved list deletes only ampm2's copy. If the app still runs in pm2 and sync is on, it will be saved again; delete it from pm2 first, or turn sync off."),
            B("Where it is stored", "%APPDATA%\\ampm2\\saved-list.json, a normal pm2 ecosystem file.", W),
            B("Where it is stored", "~/Library/Application Support/ampm2/saved-list.json, a normal pm2 ecosystem file.", M),
        }),
        ("new", "Adding a new process", "The New process window: scripts, npm scripts and ecosystem files.", new[]
        {
            P("Click New process in the header. ampm2 writes a pm2 app config from the form and runs `pm2 start` with it. The config is kept, so the Saved list records the app exactly as you entered it."),
            K("Ctrl+N", "Open New process.", W),
            K("⌘N", "Open New process.", M),
            H("Three kinds"),
            B("Script / program", "A .js, .mjs, .cjs, .ts or .py file, or any executable. Interpreter Auto picks node for JavaScript, python for .py, and runs other files directly."),
            B("npm script", "Pick a project folder; ampm2 lists the scripts in its package.json. On Windows it runs npm through Node.js, which works where pm2 with npm.cmd fails.", W),
            B("npm script", "Pick a project folder; ampm2 lists the scripts in its package.json and runs npm run <script> there.", M),
            B("Ecosystem file", "Starts an existing .config.js, .json or .yml file. Only these apps limits it to some app names (comma separated)."),
            H("Fields"),
            B("Name", "Filled in from the file or package name. It must not already exist in pm2."),
            B("Working directory", "Defaults to the script's folder."),
            B("Arguments", "Passed to the script. For npm scripts they go after --."),
            B("Node arguments", "Passed to node itself, for example --max-old-space-size=4096."),
            B("Instances", "1 runs in fork mode. 2, 4 or all cores run in cluster mode, Node.js scripts only."),
            B("Restart above memory", "pm2 restarts the app when it uses more, for example 500M or 2G."),
            B("Restart delay", "Milliseconds to wait before restarting a crashed app."),
            B("Environment variables", "One KEY=value per line. Lines starting with # are ignored."),
            B("Auto restart", "Restart the app when it exits. On by default."),
            B("Watch files", "Restart when files change. node_modules, logs and .git are ignored."),
            B("Timestamp log lines", "pm2 prefixes each log line with the time."),
            B("Save the process list afterwards", "Runs pm2 save, so the app survives a pm2 restart."),
            N("If pm2 reports an error, the window stays open and shows pm2's message, so you can fix the field and try again."),
        }),
        ("reboot", "After a reboot", "Bringing your processes back when pm2 is not running.", new[]
        {
            P("pm2 does not start by itself after a restart on Windows, and on macOS only if you set up `pm2 startup`. When the daemon is not running, ampm2 shows three buttons:"),
            B("Start pm2", "Starts an empty pm2 daemon."),
            B("Start + resurrect", "Starts pm2 and restores pm2's own saved list (dump.pm2)."),
            B("Start from Saved list", "Starts pm2 with the apps in ampm2's Saved list. Use this when pm2's own list is empty or out of date."),
            P("ampm2 keeps watching: when pm2 starts elsewhere, for example from a terminal, it connects on its own."),
            N("To have ampm2 itself start at sign-in, hidden in the tray, tick that option in the installer, or run install.ps1 -Autostart for the portable build.", W),
        }),
        ("admin", "pm2 running as administrator", "Why ampm2 may need to run elevated, and how to avoid the UAC prompt.", new[]
        {
            P("On Windows, pm2 always listens on the same named pipe. When the daemon was started from an administrator terminal, Windows only lets administrator programs talk to it, so a normal ampm2 cannot connect.", W),
            P("ampm2 detects this and shows two buttons:", W),
            B("Relaunch as admin, and don't ask again", "Approve one UAC prompt. ampm2 creates a scheduled task named \"ampm2 (elevated)\", and later launches open elevated with no prompt.", W),
            B("Just this once", "Relaunches elevated now; next time it asks again.", W),
            P("You can turn the no-prompt launch on or off in Settings (from an elevated ampm2). The installer's uninstaller removes the task.", W),
            N("A simpler setup: start pm2 from a normal terminal, or with Start pm2 in a normal ampm2. Then nothing needs administrator rights.", W),
            P("On macOS the pm2 socket belongs to your user. If ampm2 says it has no access, pm2 was probably started with sudo. Run `sudo pm2 kill`, then start pm2 as yourself (`pm2 resurrect`, or Start pm2 in ampm2).", M),
        }),
        ("strays", "Stray pm2 daemons", "The warning banner about leftover daemons.", new[]
        {
            P("When pm2 runs as administrator, every pm2 command typed in a normal terminal starts a new pm2 daemon that cannot reach the pipe and never exits. Each one holds about 40 to 50 MB and manages nothing.", W),
            P("ampm2 finds them: node processes running pm2's Daemon.js that are not the daemon it is connected to and have no child processes. A banner shows how many there are and how much memory they use.", W),
            B("Clean up…", "Ends them after you confirm. Your real daemon and your apps are never touched.", W),
            N("To stop new ones appearing, run pm2 commands from an administrator terminal while pm2 runs as administrator, or restart pm2 without administrator rights.", W),
            P("This problem does not exist on macOS.", M),
        }),
        ("settings", "Settings", "Every option explained.", new[]
        {
            B("Theme", "System follows your Windows setting; or pick Dark or Light.", W),
            B("Theme", "System follows your macOS setting; or pick Dark or Light.", M),
            B("CPU / memory sampling", "How often ampm2 measures CPU and memory while the window is visible: every 1, 2, 5 or 10 seconds. It stops while the window is hidden."),
            B("Full list refresh", "A backstop that re-reads the whole pm2 list every 30 seconds to 5 minutes. Changes normally arrive instantly over pm2's event bus; asking pm2 for its list makes pm2 run a Windows system query, so this stays infrequent.", W),
            B("Close button hides to the tray", "Closing the window keeps ampm2 running in the tray. Turn it off to quit on close.", W),
            B("Closing the window keeps ampm2 in the menu bar", "Turn it off to quit when the window closes.", M),
            B("Start hidden in the tray", "ampm2 starts without showing its window.", W),
            B("Confirm delete / stop all / flush", "Ask before destructive actions."),
            B("GPU rendering", "Off by default, which saves about 50 MB of memory. Turn it on if animations or scrolling look slow. Needs a restart.", W),
            B("Start as administrator without a UAC prompt", "See \"pm2 running as administrator\". Available when ampm2 runs elevated.", W),
        }),
        ("tray", "Tray icon", "Working from the notification area.", new[]
        {
            B("Click", "Shows or hides the window."),
            B("Right-click", "Open ampm2, Restart all, Stop all, Save process list, Help, About ampm2 and Exit."),
            B("Red dot", "At least one process is errored."),
            B("Amber dot", "ampm2 is not connected to pm2."),
            B("Tooltip", "How many processes are online, stopped and errored."),
            N("While the window is hidden ampm2 stops sampling and gives memory back, so it uses almost no CPU."),
        }),
        ("menubar", "Menu bar and Dock", "Working from the macOS menu bar.", new[]
        {
            B("Menu bar icon", "Click it to show or hide the window. Its menu has Open ampm2, Restart all, Stop all, Save process list, Help, About ampm2 and Quit ampm2."),
            B("Tooltip", "How many processes are online, stopped and errored."),
            B("Dock", "Clicking the Dock icon brings the window back after you closed it."),
            B("App menu", "ampm2 ▸ About ampm2, Settings… (⌘,) and ampm2 Help (⌘?)."),
            B("Quit", "Use Quit ampm2 in the menu bar icon's menu. Closing the window only hides it (see Settings)."),
        }),
        ("install", "Installing Node.js and pm2", "What ampm2 can install for you.", new[]
        {
            P("If pm2 is not installed, ampm2 shows two buttons:"),
            S("Install Node.js: installs Node.js LTS with winget. Windows may ask for permission. Without winget, ampm2 opens the Node.js download page.", W),
            S("Install Node.js: installs Node.js with Homebrew (brew install node). Without Homebrew, ampm2 opens the Node.js download page.", M),
            S("Install pm2: runs npm install -g pm2."),
            P("The Windows installer does the same checks on its Prerequisites page and installs whatever you leave ticked, including the .NET 10 Desktop Runtime ampm2 needs.", W),
            N("ampm2 reads your login shell's PATH, so it finds node and pm2 installed with Homebrew, nvm, volta or n, even though apps opened from Finder normally do not see them.", M),
        }),
        ("keys", "Keyboard shortcuts", "Every shortcut in one place.", new[]
        {
            K("F1", "Open this help.", W),
            K("F5", "Refresh the process list.", W),
            K("Ctrl+N", "New process.", W),
            K("Ctrl+S", "Save the process list (pm2 save), or the definition while editing one.", W),
            K("Ctrl+R", "Restart the selected process.", W),
            K("Ctrl+L", "Show the logs of the selected process.", W),
            K("Ctrl+F", "Search. Esc clears it.", W),
            K("Delete", "Delete the selected process, or remove the selected saved app.", W),
            K("Enter", "Show the logs of the selected process.", W),
            K("Double-click", "Switch between Overview and Logs.", W),
            K("Esc", "Close a dialog, Settings or About. In a confirmation, Esc cancels.", W),
            K("⌘? or F1", "Open this help.", M),
            K("⌘R or F5", "Refresh the process list.", M),
            K("⌘N", "New process.", M),
            K("⌘S", "Save the process list (pm2 save), or the definition while editing one.", M),
            K("⌘,", "Settings.", M),
            K("Double-click", "Switch between Overview and Logs.", M),
            K("Esc", "Close a dialog, Settings or About. In a confirmation, Esc cancels.", M),
        }),
        ("files", "Where ampm2 keeps its files", "Settings, the Saved list, and pm2's own files.", new[]
        {
            B("Settings", "%APPDATA%\\ampm2\\settings.json", W),
            B("Saved list", "%APPDATA%\\ampm2\\saved-list.json", W),
            B("New process configs", "%APPDATA%\\ampm2\\apps\\<name>.json", W),
            B("Error log", "%APPDATA%\\ampm2\\errors.log (only if something went wrong)", W),
            B("Settings", "~/Library/Application Support/ampm2/settings.json", M),
            B("Saved list", "~/Library/Application Support/ampm2/saved-list.json", M),
            B("New process configs", "~/Library/Application Support/ampm2/apps/<name>.json", M),
            B("pm2", "Its own folder, usually .pm2 in your home folder (or $PM2_HOME): logs, dump.pm2 and pm2.log. ⋯ ▸ Open pm2 folder opens it.", W),
            B("pm2", "Its own folder, usually ~/.pm2 (or $PM2_HOME): logs, dump.pm2 and pm2.log.", M),
            N("Uninstalling ampm2 asks whether to remove its settings. pm2 and your processes are never affected.", W),
        }),
        ("trouble", "Troubleshooting", "Common problems and what to do.", new[]
        {
            B("\"Could not talk to pm2\"", "pm2 answered in an unexpected way. Click Retry. If it persists, restart pm2 (⋯ ▸ Kill pm2 daemon, then Start + resurrect).", W),
            B("\"Could not talk to pm2\"", "pm2 answered in an unexpected way. Click Retry. If it persists, restart pm2 from a terminal (pm2 kill, then pm2 resurrect).", M),
            B("\"Not available\" when adding or saving", "ampm2 cannot reach the daemon, so it will not run pm2 commands that would start a second daemon. Fix the connection first.", W),
            B("An app keeps going to errored", "Open its Logs, then the err stream: the reason is usually the last lines before each exit."),
            B("An npm app will not start", "Use New process ▸ npm script, which avoids pm2's problem with npm.cmd on Windows.", W),
            B("Logs are not live", "If the Logs tab says the live stream is unavailable, click Reload to read the files again; live lines return once ampm2 reconnects."),
            B("CPU shows 0% for a busy app", "The first sample needs two measurements, so wait a few seconds. If the app runs through a wrapper, ampm2 already counts the whole process tree."),
            B("ampm2 will not open", "Unzip it, move it to Applications and run: xattr -dr com.apple.quarantine /Applications/ampm2.app. Or use System Settings ▸ Privacy & Security ▸ Open Anyway.", M),
            B("Reporting a problem", "About ▸ Copy details copies the version, platform and pm2 version. Paste them into a GitHub issue or an email."),
            B("Error log", "Unexpected errors are written to %APPDATA%\\ampm2\\errors.log.", W),
        }),
        ("about", "About and license", "Version, author and license.", new[]
        {
            P("About shows the version, platform, runtime, author, email, website, GitHub page and license. Copy details copies them for bug reports."),
            B("Open About", "Choose About ampm2 from the ⋯ menu or the tray menu, or use the About link at the bottom of Settings.", W),
            B("Open About", "Click the ⓘ button, choose ampm2 ▸ About ampm2, or use the menu bar icon.", M),
            H("License"),
            P("ampm2 is licensed under the PolyForm Noncommercial License 1.0.0 with an additional permission for education."),
            B("Free", "Personal and other non-commercial use."),
            B("Free for education worldwide", "Schools, colleges, universities and educational institutes, public or private, and anyone using ampm2 to learn, teach or research."),
            B("Commercial use", "Needs a license from the author: ali.mehraei.dev@gmail.com."),
        }),
    };

    private static readonly Dictionary<string, HelpPlatform> TopicPlatform = new()
    {
        ["admin"] = HelpPlatform.All, ["strays"] = HelpPlatform.Windows, ["tray"] = HelpPlatform.Windows, ["menubar"] = HelpPlatform.Mac,
    };

    public static HelpPlatform Current => OperatingSystem.IsMacOS() ? M : W;

    /// <summary>Topics for one platform, with the other platform's blocks removed and steps numbered.</summary>
    public static IReadOnlyList<HelpTopic> Topics(HelpPlatform platform)
    {
        var list = new List<HelpTopic>();
        foreach (var (id, title, summary, blocks) in All)
        {
            if (TopicPlatform.TryGetValue(id, out var tp) && (tp & platform) == 0) continue;
            var kept = blocks.Where(b => (b.Platform & platform) != 0).Select(b => new HelpBlock(b.Kind, b.Text, b.Term, b.Platform)).ToList();
            if (kept.Count == 0) continue;
            int n = 0;
            foreach (var b in kept) b.Number = b.Kind == HelpKind.Step ? ++n : 0;
            list.Add(new HelpTopic(id, title, summary, kept));
        }
        return list;
    }

    /// <summary>docs/HELP.md: both platforms, with Windows-only / macOS-only parts labelled.</summary>
    public static string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# ampm2 help").AppendLine();
        sb.AppendLine("The same guide is built into the app: press **F1** (Windows) or **⌘?** (macOS), or click the **?** button.").AppendLine();
        sb.AppendLine("Parts that apply to one platform only are marked *(Windows)* or *(macOS)*.").AppendLine();
        foreach (var (id, title, _, _) in All) sb.AppendLine($"- [{title}](#{Anchor(title)}){PlatformTag(TopicPlatform.GetValueOrDefault(id, HelpPlatform.All))}");
        sb.AppendLine();
        foreach (var (id, title, summary, blocks) in All)
        {
            sb.AppendLine($"## {title}{PlatformTag(TopicPlatform.GetValueOrDefault(id, HelpPlatform.All))}").AppendLine();
            sb.AppendLine($"*{summary}*").AppendLine();
            HelpKind? last = null;
            int step = 0;
            foreach (var b in blocks)
            {
                bool listy = b.Kind is HelpKind.Bullet or HelpKind.Step or HelpKind.Key;
                if (last is { } l && (l is HelpKind.Bullet or HelpKind.Step or HelpKind.Key) && !listy) sb.AppendLine();
                if (b.Kind != HelpKind.Step) step = 0;
                var tag = PlatformTag(b.Platform);
                switch (b.Kind)
                {
                    case HelpKind.Heading: sb.AppendLine($"### {b.Text}{tag}").AppendLine(); break;
                    case HelpKind.Paragraph: sb.AppendLine(b.Text + tag).AppendLine(); break;
                    case HelpKind.Bullet: sb.AppendLine($"- **{b.Term}**{tag}: {b.Text}"); break;
                    case HelpKind.Step: sb.AppendLine($"{++step}. {b.Text}{tag}"); break;
                    case HelpKind.Key: sb.AppendLine($"- `{b.Term}`{tag}: {b.Text}"); break;
                    case HelpKind.Note: sb.AppendLine($"> {b.Text}{tag}").AppendLine(); break;
                    case HelpKind.Code: sb.AppendLine("```").AppendLine(b.Text).AppendLine("```").AppendLine(); break;
                }
                last = b.Kind;
            }
            if (last is HelpKind.Bullet or HelpKind.Step or HelpKind.Key) sb.AppendLine();
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    private static string PlatformTag(HelpPlatform p) => p switch { HelpPlatform.Windows => " *(Windows)*", HelpPlatform.Mac => " *(macOS)*", _ => "" };

    private static string Anchor(string title) =>
        new string(title.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').ToArray()).Replace(' ', '-');
}
