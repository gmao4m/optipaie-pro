<#
    OptiPaie PRO - one-command installer build.

    Produces, in installer\output:
      * OptiPaie PRO.msi        - the MSI wizard (welcome/license/location/finish)
      * OptiPaie PRO Setup.exe  - the Setup.exe bootstrapper (installs .NET 4.8 if
                                  missing, then the app)

    Prerequisites (already present on this machine):
      * .NET SDK (dotnet)
      * WiX v5 CLI + extensions:
          dotnet tool install --global wix
          wix extension add -g WixToolset.UI.wixext
          wix extension add -g WixToolset.BootstrapperApplications.wixext
          wix extension add -g WixToolset.Netfx.wixext

    Usage:  powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
#>
$ErrorActionPreference = "Stop"
$root      = Split-Path -Parent $PSScriptRoot          # repo root
$desktopCsproj = Join-Path $root "src\OptiPaie.Desktop\OptiPaie.Desktop.csproj"
$stage     = Join-Path $env:TEMP "optipaie_publish"
$outDir    = Join-Path $PSScriptRoot "output"
$wix       = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"

Write-Host "==> Building Release payload..." -ForegroundColor Cyan
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
dotnet build $desktopCsproj -c Release -o $stage -v minimal
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }

Write-Host "==> Trimming payload (pdb, macOS natives)..." -ForegroundColor Cyan
Get-ChildItem $stage -Recurse -Include *.pdb,*.dylib | Remove-Item -Force

New-Item -ItemType Directory -Force $outDir | Out-Null

Write-Host "==> Building MSI..." -ForegroundColor Cyan
& $wix build (Join-Path $PSScriptRoot "Package.wxs") `
    -d "PublishDir=$stage" -b $PSScriptRoot `
    -ext WixToolset.UI.wixext `
    -o (Join-Path $outDir "OptiPaie PRO.msi")
if ($LASTEXITCODE -ne 0) { throw "MSI build failed." }

Write-Host "==> Building Setup.exe bootstrapper..." -ForegroundColor Cyan
& $wix build (Join-Path $PSScriptRoot "Bundle.wxs") `
    -b $PSScriptRoot `
    -ext WixToolset.BootstrapperApplications.wixext `
    -ext WixToolset.Netfx.wixext `
    -o (Join-Path $outDir "OptiPaie PRO Setup.exe")
if ($LASTEXITCODE -ne 0) { throw "Setup.exe build failed." }

Remove-Item (Join-Path $outDir "*.wixpdb") -Force -ErrorAction SilentlyContinue
Write-Host "==> Done. Deliverables in $outDir" -ForegroundColor Green
Get-ChildItem $outDir | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,2)}} | Format-Table -AutoSize
