# Regenerates the README screenshots (docs\screenshots\*.png) from a demo pm2 daemon with sample services.
#   pwsh tools\screenshots.ps1 [-DemoDir D:\srv]
# Nothing touches your real pm2: the demo daemon runs on private pipes, the app uses its own profile.
# The window pops up and is driven through UI Automation, so leave the mouse and keyboard alone while it runs.
param([string]$DemoDir = 'D:\srv')
$ErrorActionPreference = 'Stop'
# the demo folder is deleted afterwards, so it must not exist yet
if (Test-Path $DemoDir) { throw "$DemoDir already exists; pass -DemoDir with a folder that does not exist yet." }
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'docs\screenshots'
New-Item -ItemType Directory -Force $out | Out-Null
$exe = Join-Path $root 'src\Ampm2\bin\Release\net10.0-windows\ampm2.exe'
if (-not (Test-Path $exe)) { dotnet build (Join-Path $root 'src\Ampm2\Ampm2.csproj') -c Release | Out-Null }
$pm2Home = Join-Path $DemoDir '.pm2'
$profileDir = Join-Path $env:APPDATA 'ampm2-shots'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
function Cond($p, $v) { New-Object System.Windows.Automation.PropertyCondition($p, $v) }
function Win([int]$procId) { $A::RootElement.FindFirst($Scope::Children, (Cond $A::ProcessIdProperty $procId)) }
function Invoke-Id($w, $id) {
    $e = $w.FindFirst($Scope::Descendants, (Cond $A::AutomationIdProperty $id))
    if (-not $e) { throw "not found: $id" }
    $o = $null
    if ($e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$o)) { $o.Invoke() }
    elseif ($e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$o)) { $o.Select() }   # radio buttons
    else { throw "cannot activate: $id" }
}
function Invoke-Name($w, $name) {
    foreach ($e in $w.FindAll($Scope::Descendants, (Cond $A::NameProperty $name))) {
        $x = $e
        for ($k = 0; $k -lt 4 -and $x; $k++) {
            $o = $null
            if (-not $x.Current.IsOffscreen) {
                if ($x.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$o)) { $o.Invoke(); return }
                if ($x.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$o)) { $o.Select(); return }
            }
            $x = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($x)
        }
    }
    throw "not found: $name"
}
function Select-Row($w, $name) {
    foreach ($i in $w.FindAll($Scope::Descendants, (Cond $A::ControlTypeProperty ([System.Windows.Automation.ControlType]::ListItem)))) {
        if ($i.FindFirst($Scope::Descendants, (Cond $A::NameProperty $name))) {
            $i.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); return
        }
    }
    throw "row not found: $name"
}
function Shot($procId, $file, $title = 'ampm2') {
    $path = Join-Path $out $file
    for ($try = 1; $try -le 4; $try++) {
        # a window minimized in the meantime (e.g. by a click elsewhere) captures as a tiny strip: restore and retry
        $mw = Win $procId; $wp = $null
        if ($mw -and $mw.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$wp) -and $wp.Current.WindowVisualState -ne 'Normal') {
            $wp.SetWindowVisualState('Normal'); Start-Sleep -Seconds 1
        }
        pwsh -NoProfile -File (Join-Path $PSScriptRoot 'capture.ps1') -ProcessId $procId -Out $path -TitleLike $title | Out-Null
        $bytes = [IO.File]::ReadAllBytes($path)
        $width = [long]$bytes[16] * 16777216 + [long]$bytes[17] * 65536 + [long]$bytes[18] * 256 + [long]$bytes[19]   # PNG IHDR width (big-endian)
        if ($width -ge 500) { Write-Host "  $file"; return }
        if ($mw) { [void][Win32Show]::ShowWindow([IntPtr]$mw.Current.NativeWindowHandle, 9) }   # SW_RESTORE
        Start-Sleep -Seconds 2
    }
    throw "could not capture $file (window kept minimizing)"
}
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Win32Show { [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd); }
'@
function Start-App($theme) {
    New-Item -ItemType Directory -Force $profileDir | Out-Null
    @{ Theme = $theme; Width = 1180; Height = 720; DetailWidth = 420; AutoSyncSavedList = $true; ConfirmDestructive = $true } |
        ConvertTo-Json | Set-Content (Join-Path $profileDir 'settings.json')
    $p = Start-Process $exe -ArgumentList '--show' -PassThru
    $script:app = $p
    for ($i = 0; $i -lt 40 -and -not (Win $p.Id); $i++) { Start-Sleep -Milliseconds 500 }
    if (-not (Win $p.Id)) { throw 'ampm2 window did not appear (another test copy running?)' }
    Start-Sleep -Seconds 4
    return $p
}

# ---- demo daemon ----
$env:AMPM2_TEST_HOME = $pm2Home; $env:AMPM2_DEMO_DIR = $DemoDir
$daemon = Start-Process node -ArgumentList "`"$(Join-Path $PSScriptRoot 'test-daemon.js')`"" -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 12   # let the services start, the mailer crash itself into "errored", and metrics settle

$env:AMPM2_PROFILE = 'shots'; $env:AMPM2_RPC_PIPE = 'ampm2-test-rpc.sock'; $env:AMPM2_PUB_PIPE = 'ampm2-test-pub.sock'
$env:AMPM2_PM2 = Join-Path $PSScriptRoot 'test-pm2.cmd'; $env:PM2_HOME = $pm2Home
try {
    Write-Host 'dark theme'
    $app = Start-App 'Dark'; $w = Win $app.Id
    Select-Row $w 'image-resizer'; Start-Sleep -Seconds 30     # fill the CPU / memory sparklines
    Shot $app.Id 'processes-dark.png'
    Select-Row $w 'shop-api'; Start-Sleep -Seconds 1
    Invoke-Name $w 'Logs'; Start-Sleep -Seconds 4
    Shot $app.Id 'logs.png'
    Invoke-Name $w 'Overview'
    Invoke-Id $w 'ViewSaved'; Start-Sleep -Seconds 1
    Select-Row $w 'mailer'; Start-Sleep -Seconds 1
    Shot $app.Id 'saved-list.png'
    Invoke-Id $w 'ViewProcesses'; Start-Sleep -Milliseconds 500
    Invoke-Id $w 'AddButton'; Start-Sleep -Seconds 2
    $dlg = $w.FindFirst($Scope::Children, (Cond $A::ControlTypeProperty ([System.Windows.Automation.ControlType]::Window)))
    foreach ($kv in @{ ScriptBox = (Join-Path $DemoDir 'storefront\server.js'); NameBox = 'storefront-staging'; ArgsBox = '--port 8081'; EnvBox = "NODE_ENV=staging`r`nAPI_URL=http://localhost:39881"; MaxMemBox = '300M' }.GetEnumerator()) {
        $dlg.FindFirst($Scope::Descendants, (Cond $A::AutomationIdProperty $kv.Key)).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($kv.Value)
        Start-Sleep -Milliseconds 200
    }
    Shot $app.Id 'new-process.png' 'New process'
    [void]$dlg.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Seconds 1
    Invoke-Id $w 'SettingsButton'; Start-Sleep -Milliseconds 500
    Invoke-Name $w 'About'; Start-Sleep -Seconds 1
    Shot $app.Id 'about.png'
    $app | Stop-Process -Force; Start-Sleep -Seconds 1

    Write-Host 'light theme'
    $app = Start-App 'Light'; $w = Win $app.Id
    Select-Row $w 'image-resizer'; Start-Sleep -Seconds 24
    Shot $app.Id 'processes-light.png'
    $app | Stop-Process -Force
}
finally {
    if ($script:app -and -not $script:app.HasExited) { $script:app | Stop-Process -Force }
    New-Item -ItemType File -Force (Join-Path $pm2Home 'stop') | Out-Null
    Start-Sleep -Seconds 6
    if (-not $daemon.HasExited) { $daemon | Stop-Process -Force }
    if (Test-Path $profileDir) { [IO.Directory]::Delete($profileDir, $true) }
    if (Test-Path $DemoDir) { [IO.Directory]::Delete($DemoDir, $true) }
}
Write-Host "screenshots in $out"
