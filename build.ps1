# Builds ampm2 into .\dist (self-contained: no .NET install needed to run it).
#   pwsh .\build.ps1
# Installs a user-local .NET 10 SDK first if none is found (no admin rights needed).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
if (-not $dotnet -or -not ((& $dotnet --list-sdks) -match '^10\.')) {
    $dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (-not (Test-Path $dotnet) -or -not ((& $dotnet --list-sdks) -match '^10\.')) {
        Write-Host 'Installing the .NET 10 SDK to your user profile...'
        $s = Join-Path $env:TEMP 'dotnet-install.ps1'
        Invoke-WebRequest -UseBasicParsing https://dot.net/v1/dotnet-install.ps1 -OutFile $s
        & $s -Channel 10.0 -InstallDir (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet') -NoPath
    }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
$proj = Join-Path $root 'src\Ampm2\Ampm2.csproj'
$ico = Join-Path $root 'src\Ampm2\Assets\ampm2.ico'
if (-not (Test-Path $ico)) {
    # the icon is drawn in code: build once, export it, then build for real
    & $dotnet build $proj -c Release | Out-Null
    & (Join-Path $root 'src\Ampm2\bin\Release\net10.0-windows\ampm2.exe') --export-icon $ico | Out-Null
    Start-Sleep -Seconds 2
}
$dist = Join-Path $root 'dist'
& $dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $dist
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
Write-Host "`nBuilt: $dist\ampm2.exe"
