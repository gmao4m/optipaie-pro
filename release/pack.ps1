<#
    OptiPaie PRO — Velopack release pipeline.

    Builds the Release payload and produces, in release\output:
      * OptiPaiePRO-win-Setup.exe   (the installer customers run once)
      * OptiPaiePRO-<ver>-full.nupkg   (full package)
      * OptiPaiePRO-<ver>-delta.nupkg  (delta vs the previous release, if present)
      * RELEASES / assets.<channel>.json  (the manifest the app reads to self-update)

    The output folder is the update FEED — upload its contents to your host
    (e.g. a public Supabase Storage bucket) and point Update.FeedUrl (App.config)
    at it. To publish a new version, bump -Version and run again; Velopack creates
    the delta automatically from the previous release in release\output.

    Prereqs (installed): .NET SDK, and the matching vpk CLI:
        dotnet tool install -g vpk --version 1.2.0

    Usage:  powershell -ExecutionPolicy Bypass -File release\pack.ps1 -Version 1.0.0
#>
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$root    = Split-Path -Parent $PSScriptRoot
$csproj  = Join-Path $root "src\OptiPaie.Desktop\OptiPaie.Desktop.csproj"
$publish = Join-Path $env:TEMP "optipaie_vpkpublish"
$out     = Join-Path $PSScriptRoot "output"
$icon    = Join-Path $root "build\OptiPaie.ico"
$vpk     = Join-Path $env:USERPROFILE ".dotnet\tools\vpk.exe"

Write-Host "==> Building Release payload ($Version)..." -ForegroundColor Cyan
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
dotnet build $csproj -c Release -o $publish -v minimal
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }
Get-ChildItem $publish -Recurse -Include *.pdb,*.dylib | Remove-Item -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force $out | Out-Null

Write-Host "==> Velopack pack..." -ForegroundColor Cyan
& $vpk pack `
    --packId OptiPaiePRO `
    --packVersion $Version `
    --packDir $publish `
    --mainExe "OptiPaie PRO.exe" `
    --packTitle "OptiPaie PRO" `
    --packAuthors "OptiPaie" `
    --icon $icon `
    --shortcuts "Desktop,StartMenuRoot" `
    --outputDir $out
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed." }

Write-Host "==> Done. Feed artifacts in $out" -ForegroundColor Green
Get-ChildItem $out | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,2)}} | Format-Table -AutoSize
