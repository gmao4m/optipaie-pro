<#
    OptiPaie PRO Admin - one-command installer build.

    Produces installer-admin\output\OptiPaie PRO Admin.msi - a wizard that installs
    the licensing console with Desktop + Start Menu shortcuts and a distinct
    gold/navy icon. The owner's machine already has .NET Framework 4.8 (it runs the
    client), so no bootstrapper is needed.

    Prereqs (same as the client installer):
      dotnet SDK; WiX v5 CLI + WixToolset.UI.wixext/5.0.2
    Usage:  powershell -ExecutionPolicy Bypass -File installer-admin\build-admin-installer.ps1
#>
$ErrorActionPreference = "Stop"
$root      = Split-Path -Parent $PSScriptRoot          # repo root
$adminCsproj = Join-Path $root "src\OptiPaie.Admin\OptiPaie.Admin.csproj"
$stage     = Join-Path $env:TEMP "optipaie_admin_publish"
$outDir    = Join-Path $PSScriptRoot "output"
$wix       = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"

[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$appVersion = ([string]($props.Project.PropertyGroup.Version)).Trim()
if (-not $appVersion) { throw "No <Version> found in Directory.Build.props" }
$wixVersion = "$appVersion.0"
Write-Host "==> Admin version = $appVersion (WiX $wixVersion)" -ForegroundColor Cyan

Write-Host "==> Building Release payload..." -ForegroundColor Cyan
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
dotnet build $adminCsproj -c Release -o $stage -v minimal
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }

Write-Host "==> Trimming payload (pdb, macOS natives)..." -ForegroundColor Cyan
Get-ChildItem $stage -Recurse -Include *.pdb,*.dylib | Remove-Item -Force

New-Item -ItemType Directory -Force $outDir | Out-Null

Write-Host "==> Building MSI..." -ForegroundColor Cyan
& $wix build (Join-Path $PSScriptRoot "Package.wxs") `
    -d "PublishDir=$stage" -d "AppVersion=$wixVersion" -b $PSScriptRoot `
    -ext WixToolset.UI.wixext `
    -o (Join-Path $outDir "OptiPaie PRO Admin.msi")
if ($LASTEXITCODE -ne 0) { throw "MSI build failed." }

Remove-Item (Join-Path $outDir "*.wixpdb") -Force -ErrorAction SilentlyContinue
Write-Host "==> Done. Deliverable in $outDir" -ForegroundColor Green
Get-ChildItem $outDir | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,2)}} | Format-Table -AutoSize
