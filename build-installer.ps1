# Builds the Windows installer: .\setup\ampm2-setup-<version>.exe
#   pwsh .\build-installer.ps1
# The installer carries a framework-dependent build (small) and offers to install the
# .NET 10 Desktop Runtime, Node.js and pm2 when they are missing.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet) { throw 'The .NET 10 SDK is needed to build (winget install Microsoft.DotNet.SDK.10).' }
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Host 'Installing Inno Setup (per user)...'
    winget install --id JRSoftware.InnoSetup -e --scope user --silent --accept-source-agreements --accept-package-agreements
    $iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
}

$proj = Join-Path $root 'src\Ampm2\Ampm2.csproj'
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$ico = Join-Path $root 'src\Ampm2\Assets\ampm2.ico'
if (-not (Test-Path $ico)) {
    & $dotnet build $proj -c Release | Out-Null
    & (Join-Path $root 'src\Ampm2\bin\Release\net10.0-windows\ampm2.exe') --export-icon $ico | Out-Null
    Start-Sleep -Seconds 2
}

$publish = Join-Path $root 'publish'
if (Test-Path $publish) { Get-ChildItem $publish -Recurse -File | ForEach-Object { [IO.File]::Delete($_.FullName) } }
& $dotnet publish $proj -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

& $iscc "/DAppVersion=$version" (Join-Path $root 'installer\ampm2.iss')
if ($LASTEXITCODE -ne 0) { throw 'installer compile failed' }
Write-Host "`nInstaller: $(Join-Path $root "setup\ampm2-setup-$version.exe")"
