# Builds the macOS app from Windows:  pwsh .\build-mac.ps1
#   -> mac\ampm2-<version>-macos-arm64.zip  (Apple Silicon)
#   -> mac\ampm2-<version>-macos-x64.zip    (Intel)
# Each zip holds a self-contained ampm2.app (no .NET needed on the Mac), ad-hoc signed with rcodesign
# (Apple Silicon refuses to run unsigned code). For a Developer ID signature + .dmg, run build-mac.sh on a Mac.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'src\Ampm2.Mac\Ampm2.Mac.csproj'
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$icons = Join-Path $root 'src\Ampm2.Mac\Assets'
$out = Join-Path $root 'mac'
New-Item -ItemType Directory -Force $out | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'

$rcodesign = Get-ChildItem (Join-Path $root '.wsl-cache\rcodesign') -Recurse -Filter rcodesign.exe -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $rcodesign) {
    Write-Host 'Downloading rcodesign (open-source Apple code signing)...'
    $cache = Join-Path $root '.wsl-cache'; New-Item -ItemType Directory -Force $cache | Out-Null
    $rel = Invoke-RestMethod -UseBasicParsing 'https://api.github.com/repos/indygreg/apple-platform-rs/releases?per_page=10' |
        Where-Object { $_.tag_name -like 'apple-codesign/*' } | Select-Object -First 1
    $asset = $rel.assets | Where-Object { $_.name -like '*x86_64-pc-windows-msvc.zip' } | Select-Object -First 1
    Invoke-WebRequest -UseBasicParsing $asset.browser_download_url -OutFile "$cache\rcodesign.zip"
    Expand-Archive "$cache\rcodesign.zip" -DestinationPath "$cache\rcodesign" -Force
    $rcodesign = Get-ChildItem "$cache\rcodesign" -Recurse -Filter rcodesign.exe | Select-Object -First 1
}

foreach ($arch in 'arm64', 'x64') {
    $rid = "osx-$arch"
    $pub = Join-Path $root "publish-mac\$rid"
    if (Test-Path $pub) { [IO.Directory]::Delete($pub, $true) }
    Write-Host "`n== $rid"
    dotnet publish $proj -c Release -r $rid --self-contained true -p:PublishReadyToRun=false -p:UseAppHost=true -o $pub
    if ($LASTEXITCODE -ne 0) { throw "publish $rid failed" }
    $stage = Join-Path $root "publish-mac\$rid-app"
    if (Test-Path $stage) { [IO.Directory]::Delete($stage, $true) }
    New-Item -ItemType Directory -Force $stage | Out-Null
    python (Join-Path $root 'tools\mac-bundle.py') bundle $pub $icons $version $stage
    $app = Join-Path $stage 'ampm2.app'
    & $rcodesign.FullName sign $app
    if ($LASTEXITCODE -ne 0) { throw "signing $rid failed" }
    $zip = Join-Path $out "ampm2-$version-macos-$arch.zip"
    if (Test-Path $zip) { [IO.File]::Delete($zip) }
    python (Join-Path $root 'tools\mac-bundle.py') zip $app $zip
}
Copy-Item (Join-Path $root 'tools\README-mac.txt') (Join-Path $out 'README-mac.txt') -Force
Copy-Item (Join-Path $root 'LICENSE.md') (Join-Path $out 'LICENSE.md') -Force
Write-Host "`nMac builds in $out"
