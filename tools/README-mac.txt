ampm2 for macOS
===============

Which file?
  Apple Silicon (M1/M2/M3/M4…):  ampm2-<version>-macos-arm64.zip
  Intel Mac:                     ampm2-<version>-macos-x64.zip
  (Apple menu > About This Mac shows "Chip: Apple M…" or "Processor: Intel")

Install
  1. Double-click the zip. It unpacks ampm2.app.
  2. Drag ampm2.app into Applications.
  3. ampm2 is not notarized by Apple yet, so the first launch is blocked with
     "Apple could not verify…". Either:
       - run once in Terminal:   xattr -dr com.apple.quarantine /Applications/ampm2.app
       - or open System Settings > Privacy & Security, scroll down, click "Open Anyway".
  4. Open ampm2. It lives in the menu bar (the list icon) and in the Dock.

Needs macOS 14 (Sonoma) or newer. No .NET or other runtime is needed.
If Node.js or pm2 are missing, ampm2 offers to install them (Homebrew / npm).

Where things are
  Settings and the Saved list:  ~/Library/Application Support/ampm2/
  pm2 itself:                   ~/.pm2 (or $PM2_HOME)

License: PolyForm Noncommercial 1.0.0 + education permission (see LICENSE.md).
Free for non-commercial and educational use worldwide;
commercial use needs a license from the author: ali.mehraei.dev@gmail.com

Project page: https://github.com/AliMehraei/ampm2
Problems? About ampm2 > Copy details, then open an issue on GitHub or mail ali.mehraei.dev@gmail.com
