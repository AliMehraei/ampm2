# Changelog

All notable changes to ampm2 are recorded here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.2.0] - 2026-09-25

### Added
- **Built-in help guide** in both apps: 16 topics on Windows and 15 on macOS, from getting started to troubleshooting, with search. Open it with F1 (Windows), ⌘? or F1 (macOS), the **?** button in the header, the ⋯ menu, the tray or menu bar icon, or ampm2 ▸ ampm2 Help on macOS.
- **Learn more links** that open the matching help topic: on the "pm2 runs as administrator" screen, the stray-daemon banner, the "daemon not running" screen and the empty Saved list.
- **docs/HELP.md**, the same guide for reading on GitHub, generated from the in-app content with `ampm2 --export-help` (macOS app).
- This changelog.

### Changed
- The version is 1.2.0 in both apps.

## [1.1.0] - 2026-09-25

### Added
- **macOS app** (Avalonia), for Apple Silicon and Intel, macOS 14 or newer. It has the same features as the Windows app, lives in the menu bar and the Dock, and adds ampm2 ▸ About ampm2 and Settings… to the app menu. It needs no .NET install, and it reads your login shell's PATH, so it finds node and pm2 installed with Homebrew, nvm, volta or n.
- **Shared core** used by both apps: pm2's protocol over Windows named pipes and Unix sockets, process-tree CPU and memory measured through native Windows calls or `ps`, the Saved list, and a self-test (`--selftest`).
- **Saved list**: ampm2's own copy of the process list.
  - Keeps itself in sync with pm2, and never drops apps pm2 has forgotten.
  - Starts missing apps, individually or all at once.
  - Imports ecosystem `.json` / `.config.js` files and pm2's `dump.pm2`, and exports `ecosystem.json`.
  - Lets you edit each app's definition.
  - After a reboot, the "daemon not running" screen offers **Start from Saved list**.
- **About section** in both apps: author, email, website, GitHub page, version, platform, runtime and license, plus **Copy details** for bug reports.
- **Windows installer**:
  - A license agreement page.
  - A Prerequisites page that offers to install the .NET 10 Desktop Runtime, Node.js and pm2.
  - Optional desktop shortcut and start at sign-in.
  - An uninstaller that also removes the no-prompt elevated-launch task.
- **macOS builds** from Windows (`build-mac.ps1`, ad-hoc signed zips) and on a Mac (`build-mac.sh`, a `.dmg`, optional Developer ID signing).
- **License**: PolyForm Noncommercial 1.0.0 with an additional permission for education. It is free for non-commercial use, and free for schools, universities, educational institutes and individual learners worldwide. Commercial use needs a license.
- **README** with screenshots, generated from demo services by `tools/screenshots.ps1`.
- Accessible names for icon-only buttons, for screen readers and VoiceOver.

### Changed
- An existing no-prompt elevated-launch task is used only if it launches the same copy of ampm2. The installer re-points a task left by the portable build.
- The version is 1.1.0 in both apps.

### Fixed
- Apps started through ampm2 no longer receive extra `FORCE_COLOR` and `NO_COLOR` environment variables.
- Saved definitions keep only an app's own environment variables. System, shell and terminal variables are dropped, including tokens exported in the terminal that started pm2.
- In the macOS app, process details no longer show through the Saved list view, and the view switcher works from the keyboard.

## [1.0.0] - 2026-09-25

First version, for Windows only. It was not published on GitHub.

### Added
- **Process list**: live status, CPU and memory for each app's whole process tree, uptime, restarts, and cluster and namespace badges, with search, filters and sorting.
- **Actions**: start, stop, restart, reload, delete and reset counters, from row buttons, the right-click menu, the details pane, or for several selected rows at once. Also restart all, stop all, save, resurrect, flush logs and kill the daemon.
- **Details pane**: CPU and memory sparklines and every process setting, plus live logs with history, stream filters, pause and copy.
- **New process** window for scripts and programs, npm scripts (run through Node.js, so they work on Windows) and ecosystem files. It covers environment variables, cluster instances, memory limit, restart delay, watch and timestamps.
- **pm2 running as administrator**: detects the elevated daemon and relaunches ampm2 elevated, optionally without a UAC prompt through a scheduled task.
- **Stray daemon cleanup**: finds the leftover pm2 daemons that non-admin pm2 commands leave behind, and ends them.
- **Tray icon** with a status badge and a menu, a single-instance window, and dark, light and system themes.
- **Installing Node.js and pm2** from inside the app (winget and npm).
- Talks to pm2's protocol directly: no `pm2 jlist` polling, software rendering by default, and no sampling while hidden.
- Destructive confirmations open with Cancel focused, and dialogs never steal focus from other apps.

[Unreleased]: https://github.com/AliMehraei/ampm2/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/AliMehraei/ampm2/releases/tag/v1.2.0
[1.1.0]: https://github.com/AliMehraei/ampm2/releases/tag/v1.1.0
[1.0.0]: https://github.com/AliMehraei/ampm2/blob/main/CHANGELOG.md#100---2026-09-25
