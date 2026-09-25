# ampm2 help

The same guide is built into the app: press **F1** (Windows) or **⌘?** (macOS), or click the **?** button.

Parts that apply to one platform only are marked *(Windows)* or *(macOS)*.

- [Getting started](#getting-started)
- [The process list](#the-process-list)
- [Starting, stopping and restarting](#starting-stopping-and-restarting)
- [Details and live logs](#details-and-live-logs)
- [The Saved list](#the-saved-list)
- [Adding a new process](#adding-a-new-process)
- [After a reboot](#after-a-reboot)
- [pm2 running as administrator](#pm2-running-as-administrator)
- [Stray pm2 daemons](#stray-pm2-daemons) *(Windows)*
- [Settings](#settings)
- [Tray icon](#tray-icon) *(Windows)*
- [Menu bar and Dock](#menu-bar-and-dock) *(macOS)*
- [Installing Node.js and pm2](#installing-nodejs-and-pm2)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Where ampm2 keeps its files](#where-ampm2-keeps-its-files)
- [Troubleshooting](#troubleshooting)
- [About and license](#about-and-license)

## Getting started

*What ampm2 is and how to begin.*

ampm2 is a desktop manager for pm2, the Node.js process manager. It shows every process pm2 runs, with live status, CPU, memory, uptime and restarts. You can start, stop, restart and delete processes, read their logs, and add new ones, without typing pm2 commands.

ampm2 talks to the pm2 daemon directly over pm2's own connection, so it stays light: it never runs `pm2 list` in the background, and it uses almost no CPU while its window is hidden.

### First launch

1. Open ampm2. It finds the pm2 daemon on its own and shows its processes.
2. If pm2 is not installed, ampm2 offers to install Node.js and pm2 for you. See "Installing Node.js and pm2".
3. If the pm2 daemon is not running, click Start pm2. See "After a reboot" to bring your processes back.
4. If pm2 runs as administrator, ampm2 asks to relaunch elevated. See "pm2 running as administrator". *(Windows)*
5. Select a process to see its details and logs on the right.

### The window

- **Header**: The connection status, the counters for online, stopped and errored processes, total CPU and memory, and the main buttons.
- **Process list**: Everything pm2 runs. Switch to the Saved list with the tabs above it.
- **Details pane**: Overview and logs of the selected process, or the definition of the selected saved app.
- **Tray icon** *(Windows)*: ampm2 keeps running in the notification area when you close the window.
- **Menu bar icon** *(macOS)*: ampm2 keeps running in the menu bar when you close the window.

## The process list

*Columns, status colours, search, filters and sorting.*

### Columns

- **Name**: The pm2 app name, with badges for cluster mode ("cluster ×2") and a namespace other than default. The line below shows the script path.
- **Status**: online, stopped, errored, launching or waiting (restart pending).
- **CPU**: Percent of one core, like pm2 and top: 200% means two full cores. It covers the app's whole process tree, because on Windows pm2 often runs the real server as a child of a cmd or bash wrapper.
- **Memory**: Memory used by the app's whole process tree. Hover it to see how many processes the tree has.
- **Uptime**: Time since the process last started.
- **↻ (restarts)** *(Windows)*: How often pm2 restarted it. Amber when it is above zero. Reset it with Reset restart counter.
- **↻ (restarts)** *(macOS)*: How often pm2 restarted it. Amber when it is above zero.
- **ID**: pm2's id. Each cluster instance has its own id, so a cluster app appears once per instance.

### Status colours

- **Green**: online.
- **Grey**: stopped.
- **Red**: errored: pm2 gave up after too many quick crashes. Open its logs to see why.
- **Amber**: starting, or waiting to restart.

### Finding processes

- **Search** *(Windows)*: Type part of a name, an id or a script path. Press Ctrl+F to jump to the search box and Esc to clear it.
- **Search** *(macOS)*: Type part of a name, an id or a script path.
- **Filters**: All, Online, Stopped or Errored.
- **Sort**: Click a column header to sort by it. Click it again to reverse the order. CPU and memory sort highest first.

> Status changes appear instantly, because ampm2 listens to pm2's event bus. CPU and memory are sampled every 2 seconds while the window is visible; change that in Settings.

## Starting, stopping and restarting

*Controlling processes, one at a time or together.*

- **Start**: Starts a stopped or errored process.
- **Stop**: Stops (pauses) a process. pm2 keeps it in its list, so you can start it again.
- **Restart**: Stops and starts it again. The restart counter goes up by one.
- **Reload**: A graceful reload. In cluster mode pm2 replaces instances one by one, with no downtime. In fork mode it behaves like a restart.
- **Delete**: Stops the process and removes it from pm2. Its script and log files stay on disk, and it stays in the Saved list so you can start it again later.

### Where to find them

- **Row buttons**: Hover a row, or select it, to show Start or Stop, Restart, Logs and Delete.
- **Right-click a row** *(Windows)*: Start, Stop, Restart, Reload, Logs, Open working folder, Reset restart counter, Flush logs and Delete.
- **Right-click a row** *(macOS)*: Start, Stop, Restart, Reload, Logs, Show working folder, Flush logs and Delete.
- **Details pane**: Start or Stop, Restart, Reload and Delete for the selected process.
- **Header**: Save the process list, Restart all and Stop all.

### Several processes at once *(Windows)*

Select several rows with Ctrl+click or Shift+click. A bar appears below the list with Start, Stop, Restart and Delete for all of them. *(Windows)*

### Safety

Delete, Stop all and Flush logs ask for confirmation. The confirmation opens with Cancel selected, so pressing Enter by accident never deletes anything. Turn confirmations off in Settings if you prefer.

## Details and live logs

*The Overview and Logs tabs.*

### Overview

The CPU and memory graphs cover the last 60 samples. Below them are the script, working folder, arguments, interpreter, start and creation times, restarts, process tree, auto-restart, watch, memory limit, versions, log file paths and the user pm2 runs as. You can select and copy any value.

- **Log paths** *(Windows)*: The folder button next to a log path shows the file in Explorer.
- **Log paths** *(macOS)*: The folder button in the Logs tab shows the log file in Finder.

### Logs

The Logs tab first shows the end of the stdout and stderr files, then a "live" marker, then new lines as the app writes them. stderr lines are red.

- **all · out · err**: Show both streams, or only one.
- **Pause**: Freezes the view. New lines are kept and appear when you resume.
- **Reload**: Reads the log files again.
- **Clear**: Empties the view. The log files are not touched.
- **Open log file** *(Windows)*: Opens the stdout log in your default editor.
- **Follow**: The view follows new lines while you are at the bottom. Scroll up to read, and it stays put.
- **Flush logs** *(macOS)*: Empties the log files themselves (pm2 flush). Right-click a process to flush its logs.
- **Flush logs** *(Windows)*: Empties the log files themselves (pm2 flush). Right-click a process to flush its logs, or use ⋯ ▸ Flush all logs.

### Keyboard

- `Ctrl+C` *(Windows)*: Copy the selected log lines.
- `Ctrl+A` *(Windows)*: Select all log lines.
- `End` *(Windows)*: Jump to the newest line.
- `⌘C` *(macOS)*: Copy the selected log lines.

## The Saved list

*ampm2's own copy of your process list.*

pm2 keeps its list in dump.pm2, but that file is easy to lose: pm2 does not start with the computer, and running `pm2 save` after `pm2 kill` saves an empty list. The Saved list is ampm2's own copy of every app definition. It survives all of that, and it can bring apps back that pm2 has forgotten.

### How it stays up to date

- **Keep in sync with pm2**: On by default. Every app pm2 runs is copied into the Saved list when it appears or changes.
- **Never removes**: When an app disappears from pm2, it stays in the Saved list and shows "not in pm2", with a Start button.
- **Save from pm2**: Copies the current pm2 list right away, even with sync off.

### Getting apps back

- **Start missing**: Starts every saved app pm2 does not have, then saves pm2's list.
- **Start in pm2**: Starts one saved app. Use the row button, the right-click menu or the details pane.

### Import and export

- **Import…**: Reads an ecosystem .json, .config.js or .cjs file (evaluated with Node.js), or pm2's own dump.pm2. Relative paths are resolved against the file, as pm2 does. If a name already exists, ampm2 asks before replacing it.
- **Export all…**: Writes every saved app to an ecosystem.json. Start it anywhere with `pm2 start ecosystem.json`.
- **Export…**: On a row or in the details pane, exports just that app.

### Editing a definition

Select a saved app to see its definition as pm2 ecosystem JSON. Edit it and click Save changes to change how it starts next time; Revert undoes your edits. The name and script fields are required. Changes apply the next time you start the app from the Saved list.

- `Ctrl+S` *(Windows)*: Save the definition while editing it.
- `⌘S` *(macOS)*: Save the definition while editing it.

### Environment variables

pm2 copies the whole environment into every app: PATH, your user folders, and any tokens exported in the terminal that started pm2. The Saved list keeps only the app's own variables. It drops anything that equals your system environment and known shell or terminal variables. Apps you created with New process keep their exact original settings.

### Removing

Remove from Saved list deletes only ampm2's copy. If the app still runs in pm2 and sync is on, it will be saved again; delete it from pm2 first, or turn sync off.

- **Where it is stored** *(Windows)*: %APPDATA%\ampm2\saved-list.json, a normal pm2 ecosystem file.
- **Where it is stored** *(macOS)*: ~/Library/Application Support/ampm2/saved-list.json, a normal pm2 ecosystem file.

## Adding a new process

*The New process window: scripts, npm scripts and ecosystem files.*

Click New process in the header. ampm2 writes a pm2 app config from the form and runs `pm2 start` with it. The config is kept, so the Saved list records the app exactly as you entered it.

- `Ctrl+N` *(Windows)*: Open New process.
- `⌘N` *(macOS)*: Open New process.

### Three kinds

- **Script / program**: A .js, .mjs, .cjs, .ts or .py file, or any executable. Interpreter Auto picks node for JavaScript, python for .py, and runs other files directly.
- **npm script** *(Windows)*: Pick a project folder; ampm2 lists the scripts in its package.json. On Windows it runs npm through Node.js, which works where pm2 with npm.cmd fails.
- **npm script** *(macOS)*: Pick a project folder; ampm2 lists the scripts in its package.json and runs npm run <script> there.
- **Ecosystem file**: Starts an existing .config.js, .json or .yml file. Only these apps limits it to some app names (comma separated).

### Fields

- **Name**: Filled in from the file or package name. It must not already exist in pm2.
- **Working directory**: Defaults to the script's folder.
- **Arguments**: Passed to the script. For npm scripts they go after --.
- **Node arguments**: Passed to node itself, for example --max-old-space-size=4096.
- **Instances**: 1 runs in fork mode. 2, 4 or all cores run in cluster mode, Node.js scripts only.
- **Restart above memory**: pm2 restarts the app when it uses more, for example 500M or 2G.
- **Restart delay**: Milliseconds to wait before restarting a crashed app.
- **Environment variables**: One KEY=value per line. Lines starting with # are ignored.
- **Auto restart**: Restart the app when it exits. On by default.
- **Watch files**: Restart when files change. node_modules, logs and .git are ignored.
- **Timestamp log lines**: pm2 prefixes each log line with the time.
- **Save the process list afterwards**: Runs pm2 save, so the app survives a pm2 restart.

> If pm2 reports an error, the window stays open and shows pm2's message, so you can fix the field and try again.

## After a reboot

*Bringing your processes back when pm2 is not running.*

pm2 does not start by itself after a restart on Windows, and on macOS only if you set up `pm2 startup`. When the daemon is not running, ampm2 shows three buttons:

- **Start pm2**: Starts an empty pm2 daemon.
- **Start + resurrect**: Starts pm2 and restores pm2's own saved list (dump.pm2).
- **Start from Saved list**: Starts pm2 with the apps in ampm2's Saved list. Use this when pm2's own list is empty or out of date.

ampm2 keeps watching: when pm2 starts elsewhere, for example from a terminal, it connects on its own.

> To have ampm2 itself start at sign-in, hidden in the tray, tick that option in the installer, or run install.ps1 -Autostart for the portable build. *(Windows)*

## pm2 running as administrator

*Why ampm2 may need to run elevated, and how to avoid the UAC prompt.*

On Windows, pm2 always listens on the same named pipe. When the daemon was started from an administrator terminal, Windows only lets administrator programs talk to it, so a normal ampm2 cannot connect. *(Windows)*

ampm2 detects this and shows two buttons: *(Windows)*

- **Relaunch as admin, and don't ask again** *(Windows)*: Approve one UAC prompt. ampm2 creates a scheduled task named "ampm2 (elevated)", and later launches open elevated with no prompt.
- **Just this once** *(Windows)*: Relaunches elevated now; next time it asks again.

You can turn the no-prompt launch on or off in Settings (from an elevated ampm2). The installer's uninstaller removes the task. *(Windows)*

> A simpler setup: start pm2 from a normal terminal, or with Start pm2 in a normal ampm2. Then nothing needs administrator rights. *(Windows)*

On macOS the pm2 socket belongs to your user. If ampm2 says it has no access, pm2 was probably started with sudo. Run `sudo pm2 kill`, then start pm2 as yourself (`pm2 resurrect`, or Start pm2 in ampm2). *(macOS)*

## Stray pm2 daemons *(Windows)*

*The warning banner about leftover daemons.*

When pm2 runs as administrator, every pm2 command typed in a normal terminal starts a new pm2 daemon that cannot reach the pipe and never exits. Each one holds about 40 to 50 MB and manages nothing. *(Windows)*

ampm2 finds them: node processes running pm2's Daemon.js that are not the daemon it is connected to and have no child processes. A banner shows how many there are and how much memory they use. *(Windows)*

- **Clean up…** *(Windows)*: Ends them after you confirm. Your real daemon and your apps are never touched.

> To stop new ones appearing, run pm2 commands from an administrator terminal while pm2 runs as administrator, or restart pm2 without administrator rights. *(Windows)*

This problem does not exist on macOS. *(macOS)*

## Settings

*Every option explained.*

- **Theme** *(Windows)*: System follows your Windows setting; or pick Dark or Light.
- **Theme** *(macOS)*: System follows your macOS setting; or pick Dark or Light.
- **CPU / memory sampling**: How often ampm2 measures CPU and memory while the window is visible: every 1, 2, 5 or 10 seconds. It stops while the window is hidden.
- **Full list refresh** *(Windows)*: A backstop that re-reads the whole pm2 list every 30 seconds to 5 minutes. Changes normally arrive instantly over pm2's event bus; asking pm2 for its list makes pm2 run a Windows system query, so this stays infrequent.
- **Close button hides to the tray** *(Windows)*: Closing the window keeps ampm2 running in the tray. Turn it off to quit on close.
- **Closing the window keeps ampm2 in the menu bar** *(macOS)*: Turn it off to quit when the window closes.
- **Start hidden in the tray** *(Windows)*: ampm2 starts without showing its window.
- **Confirm delete / stop all / flush**: Ask before destructive actions.
- **GPU rendering** *(Windows)*: Off by default, which saves about 50 MB of memory. Turn it on if animations or scrolling look slow. Needs a restart.
- **Start as administrator without a UAC prompt** *(Windows)*: See "pm2 running as administrator". Available when ampm2 runs elevated.

## Tray icon *(Windows)*

*Working from the notification area.*

- **Click**: Shows or hides the window.
- **Right-click**: Open ampm2, Restart all, Stop all, Save process list, Help, About ampm2 and Exit.
- **Red dot**: At least one process is errored.
- **Amber dot**: ampm2 is not connected to pm2.
- **Tooltip**: How many processes are online, stopped and errored.

> While the window is hidden ampm2 stops sampling and gives memory back, so it uses almost no CPU.

## Menu bar and Dock *(macOS)*

*Working from the macOS menu bar.*

- **Menu bar icon**: Click it to show or hide the window. Its menu has Open ampm2, Restart all, Stop all, Save process list, Help, About ampm2 and Quit ampm2.
- **Tooltip**: How many processes are online, stopped and errored.
- **Dock**: Clicking the Dock icon brings the window back after you closed it.
- **App menu**: ampm2 ▸ About ampm2, Settings… (⌘,) and ampm2 Help (⌘?).
- **Quit**: Use Quit ampm2 in the menu bar icon's menu. Closing the window only hides it (see Settings).

## Installing Node.js and pm2

*What ampm2 can install for you.*

If pm2 is not installed, ampm2 shows two buttons:

1. Install Node.js: installs Node.js LTS with winget. Windows may ask for permission. Without winget, ampm2 opens the Node.js download page. *(Windows)*
2. Install Node.js: installs Node.js with Homebrew (brew install node). Without Homebrew, ampm2 opens the Node.js download page. *(macOS)*
3. Install pm2: runs npm install -g pm2.

The Windows installer does the same checks on its Prerequisites page and installs whatever you leave ticked, including the .NET 10 Desktop Runtime ampm2 needs. *(Windows)*

> ampm2 reads your login shell's PATH, so it finds node and pm2 installed with Homebrew, nvm, volta or n, even though apps opened from Finder normally do not see them. *(macOS)*

## Keyboard shortcuts

*Every shortcut in one place.*

- `F1` *(Windows)*: Open this help.
- `F5` *(Windows)*: Refresh the process list.
- `Ctrl+N` *(Windows)*: New process.
- `Ctrl+S` *(Windows)*: Save the process list (pm2 save), or the definition while editing one.
- `Ctrl+R` *(Windows)*: Restart the selected process.
- `Ctrl+L` *(Windows)*: Show the logs of the selected process.
- `Ctrl+F` *(Windows)*: Search. Esc clears it.
- `Delete` *(Windows)*: Delete the selected process, or remove the selected saved app.
- `Enter` *(Windows)*: Show the logs of the selected process.
- `Double-click` *(Windows)*: Switch between Overview and Logs.
- `Esc` *(Windows)*: Close a dialog, Settings or About. In a confirmation, Esc cancels.
- `⌘? or F1` *(macOS)*: Open this help.
- `⌘R or F5` *(macOS)*: Refresh the process list.
- `⌘N` *(macOS)*: New process.
- `⌘S` *(macOS)*: Save the process list (pm2 save), or the definition while editing one.
- `⌘,` *(macOS)*: Settings.
- `Double-click` *(macOS)*: Switch between Overview and Logs.
- `Esc` *(macOS)*: Close a dialog, Settings or About. In a confirmation, Esc cancels.

## Where ampm2 keeps its files

*Settings, the Saved list, and pm2's own files.*

- **Settings** *(Windows)*: %APPDATA%\ampm2\settings.json
- **Saved list** *(Windows)*: %APPDATA%\ampm2\saved-list.json
- **New process configs** *(Windows)*: %APPDATA%\ampm2\apps\<name>.json
- **Error log** *(Windows)*: %APPDATA%\ampm2\errors.log (only if something went wrong)
- **Settings** *(macOS)*: ~/Library/Application Support/ampm2/settings.json
- **Saved list** *(macOS)*: ~/Library/Application Support/ampm2/saved-list.json
- **New process configs** *(macOS)*: ~/Library/Application Support/ampm2/apps/<name>.json
- **pm2** *(Windows)*: Its own folder, usually .pm2 in your home folder (or $PM2_HOME): logs, dump.pm2 and pm2.log. ⋯ ▸ Open pm2 folder opens it.
- **pm2** *(macOS)*: Its own folder, usually ~/.pm2 (or $PM2_HOME): logs, dump.pm2 and pm2.log.

> Uninstalling ampm2 asks whether to remove its settings. pm2 and your processes are never affected. *(Windows)*

## Troubleshooting

*Common problems and what to do.*

- **"Could not talk to pm2"** *(Windows)*: pm2 answered in an unexpected way. Click Retry. If it persists, restart pm2 (⋯ ▸ Kill pm2 daemon, then Start + resurrect).
- **"Could not talk to pm2"** *(macOS)*: pm2 answered in an unexpected way. Click Retry. If it persists, restart pm2 from a terminal (pm2 kill, then pm2 resurrect).
- **"Not available" when adding or saving** *(Windows)*: ampm2 cannot reach the daemon, so it will not run pm2 commands that would start a second daemon. Fix the connection first.
- **An app keeps going to errored**: Open its Logs, then the err stream: the reason is usually the last lines before each exit.
- **An npm app will not start** *(Windows)*: Use New process ▸ npm script, which avoids pm2's problem with npm.cmd on Windows.
- **Logs are not live**: If the Logs tab says the live stream is unavailable, click Reload to read the files again; live lines return once ampm2 reconnects.
- **CPU shows 0% for a busy app**: The first sample needs two measurements, so wait a few seconds. If the app runs through a wrapper, ampm2 already counts the whole process tree.
- **ampm2 will not open** *(macOS)*: Unzip it, move it to Applications and run: xattr -dr com.apple.quarantine /Applications/ampm2.app. Or use System Settings ▸ Privacy & Security ▸ Open Anyway.
- **Reporting a problem**: About ▸ Copy details copies the version, platform and pm2 version. Paste them into a GitHub issue or an email.
- **Error log** *(Windows)*: Unexpected errors are written to %APPDATA%\ampm2\errors.log.

## About and license

*Version, author and license.*

About shows the version, platform, runtime, author, email, website, GitHub page and license. Copy details copies them for bug reports.

- **Open About** *(Windows)*: Choose About ampm2 from the ⋯ menu or the tray menu, or use the About link at the bottom of Settings.
- **Open About** *(macOS)*: Click the ⓘ button, choose ampm2 ▸ About ampm2, or use the menu bar icon.

### License

ampm2 is licensed under the PolyForm Noncommercial License 1.0.0 with an additional permission for education.

- **Free**: Personal and other non-commercial use.
- **Free for education worldwide**: Schools, colleges, universities and educational institutes, public or private, and anyone using ampm2 to learn, teach or research.
- **Commercial use**: Needs a license from the author: ali.mehraei.dev@gmail.com.
