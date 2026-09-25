# Adds ampm2 to the Start menu (and optionally the desktop / sign-in). No admin rights needed.
#   pwsh .\install.ps1                 Start menu shortcut
#   pwsh .\install.ps1 -Desktop        + desktop shortcut
#   pwsh .\install.ps1 -Autostart      + start hidden in the tray when you sign in
#   pwsh .\install.ps1 -Uninstall      remove shortcuts, autostart and the elevated-launch task
param([switch]$Desktop, [switch]$Autostart, [switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'dist\ampm2.exe'
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'ampm2.lnk'
$desktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ampm2.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

if ($Uninstall) {
    foreach ($l in $startMenu, $desktopLnk) { if (Test-Path $l) { [IO.File]::Delete($l); "removed $l" } }
    if (Get-ItemProperty $runKey -Name ampm2 -ErrorAction SilentlyContinue) { Remove-ItemProperty $runKey -Name ampm2; 'removed autostart' }
    & schtasks.exe '/Query' '/TN' 'ampm2 (elevated)' *> $null
    if ($LASTEXITCODE -eq 0) { 'The elevated-launch task exists: remove it from ampm2 Settings (as admin) or run:  schtasks /Delete /TN "ampm2 (elevated)" /F  in an admin terminal' }
    return
}

if (-not (Test-Path $exe)) { & (Join-Path $PSScriptRoot 'build.ps1') }

function New-Shortcut($path) {
    $sh = New-Object -ComObject WScript.Shell
    $l = $sh.CreateShortcut($path)
    $l.TargetPath = $exe
    $l.WorkingDirectory = Split-Path $exe
    $l.IconLocation = "$exe,0"
    $l.Description = 'ampm2 - pm2 process manager'
    $l.Save()
    "created $path"
}
New-Shortcut $startMenu
if ($Desktop) { New-Shortcut $desktopLnk }
if ($Autostart) {
    Set-ItemProperty $runKey -Name ampm2 -Value "`"$exe`" --autostart"
    'ampm2 will start hidden in the tray when you sign in'
}
