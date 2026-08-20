<#
    OptiPaie PRO - one-command installer build.

    Produces, in installer\output:
      * OptiPaie PRO.msi        - the MSI wizard (welcome/license/location/finish)
      * OptiPaie PRO Setup.exe  - the Setup.exe bootstrapper (installs .NET 4.8 if
                                  missing, then the app)

    Prerequisites (already present on this machine):
      * .NET SDK (dotnet)
      * WiX v5 CLI + extensions (pin extensions to the CLI version, else the
        latest 7.0.0 = WiX v6 is pulled and fails with WIX6101):
          dotnet tool install --global wix --version 5.0.2
          wix extension add -g WixToolset.UI.wixext/5.0.2
          wix extension add -g WixToolset.BootstrapperApplications.wixext/5.0.2
          wix extension add -g WixToolset.Netfx.wixext/5.0.2

    Usage:  powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
#>
$ErrorActionPreference = "Stop"
$root      = Split-Path -Parent $PSScriptRoot          # repo root
$desktopCsproj = Join-Path $root "src\OptiPaie.Desktop\OptiPaie.Desktop.csproj"
$stage     = Join-Path $env:TEMP "optipaie_publish"
$outDir    = Join-Path $PSScriptRoot "output"
$wix       = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"

# Single source of truth for the version: Directory.Build.props <Version>.
# WiX needs a 4-part version (X.Y.Z.0); Package.wxs + Bundle.wxs read $(var.AppVersion).
[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$appVersion = ([string]($props.Project.PropertyGroup.Version)).Trim()
if (-not $appVersion) { throw "No <Version> found in Directory.Build.props" }
$wixVersion = "$appVersion.0"
Write-Host "==> Product version = $appVersion (WiX $wixVersion)" -ForegroundColor Cyan

Write-Host "==> Building Release payload..." -ForegroundColor Cyan
# NOTE: 'dotnet build -o' (not 'publish'). For this .NET Framework app, 'dotnet publish'
# DROPS the System.Data.SQLite native interop (SQLite.Interop.dll under x86/x64), which the
# app needs at startup - 'build' copies it correctly. Payload completeness is enforced below
# by two guards (managed closure + native interops) rather than by the build verb.
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
dotnet build $desktopCsproj -c Release -o $stage -v minimal
if ($LASTEXITCODE -ne 0) { throw "Release build failed." }

Write-Host "==> Trimming payload (pdb, macOS natives)..." -ForegroundColor Cyan
Get-ChildItem $stage -Recurse -Include *.pdb,*.dylib | Remove-Item -Force

# ---------------------------------------------------------------------------
# RELEASE GUARD - runtime dependency closure MUST be complete.
# The 1.29.0 incident: the app crashed at startup on clients because a required
# assembly (Newtonsoft.Json 13.0.0.0, used by licensing at launch) was absent
# from the client's runtime folder, and the dev machine masked it. This guard
# walks the app's ACTUAL reference closure (derived from the binaries - never a
# hand-kept list that goes stale) and refuses to build if any non-framework
# dependency is missing from the payload.
Write-Host "==> Release guard: verifying the runtime dependency closure is complete..." -ForegroundColor Cyan
$seen    = @{}
$missing = New-Object System.Collections.Generic.List[string]
function Test-FrameworkAssembly([string]$n) {
    # .NET Framework / WPF assemblies (present on every machine, never shipped)...
    if ($n -match '^(System($|\.)|Microsoft($|\.)|mscorlib$|netstandard$|WindowsBase$|PresentationCore$|PresentationFramework($|\.)|PresentationUI$|UIAutomation|Accessibility$|WindowsFormsIntegration$|ReachFramework$|DirectWriteForwarder$|Windows($|\.))') { return $true }
    # ...and embedded-interop COM PIAs: their types are embedded into the referencing
    # assembly at compile time, so the interop DLL is intentionally NOT shipped (and the app
    # runs without it - it is absent from bin\Release too). e.g. office, stdole, VBIDE.
    if ($n -match '^(office$|stdole$|VBIDE$|Microsoft\.Vbe($|\.))') { return $true }
    return $false
}
function Test-DependencyClosure([System.Reflection.Assembly]$asm) {
    foreach ($r in $asm.GetReferencedAssemblies()) {
        $name = $r.Name
        if ($seen.ContainsKey($name)) { continue }
        $seen[$name] = $true
        $dll = Join-Path $stage ($name + '.dll')
        if (Test-Path $dll) {
            try { Test-DependencyClosure ([System.Reflection.Assembly]::ReflectionOnlyLoadFrom($dll)) } catch { }
        } elseif (-not (Test-FrameworkAssembly $name)) {
            [void]$missing.Add($name)
        }
    }
}
$exePath = Join-Path $stage 'OptiPaie PRO.exe'
Test-DependencyClosure ([System.Reflection.Assembly]::ReflectionOnlyLoadFrom($exePath))
if ($missing.Count -gt 0) {
    throw ("RELEASE GUARD FAILED: runtime dependencies missing from the installer payload: " +
           (($missing | Sort-Object -Unique) -join ', ') +
           ". Shipping this would crash clients at startup (the 1.29.0 Newtonsoft.Json class of bug). Refusing to build.")
}
Write-Host ("    OK - dependency closure complete ($($seen.Count) references checked; all present or framework).") -ForegroundColor Green

# Native interops have NO managed assembly reference, so the closure walk above cannot see
# them - yet the app dies at startup without them. Assert the known-critical natives are in
# the payload (SQLite.Interop = database; libSkiaSharp/libHarfBuzzSharp = QuestPDF payslips).
Write-Host "==> Release guard: verifying native interops are present..." -ForegroundColor Cyan
$nativeMissing = New-Object System.Collections.Generic.List[string]
foreach ($native in @("SQLite.Interop.dll","libSkiaSharp.dll","libHarfBuzzSharp.dll")) {
    $hits = Get-ChildItem $stage -Recurse -Filter $native -ErrorAction SilentlyContinue
    if (-not $hits) { [void]$nativeMissing.Add($native) } else { Write-Host ("    OK - " + $native) -ForegroundColor Green }
}
if ($nativeMissing.Count -gt 0) {
    throw ("RELEASE GUARD FAILED: native interop(s) missing from the installer payload: " +
           ($nativeMissing -join ', ') + ". The app cannot start without them. Refusing to build.")
}
# ---------------------------------------------------------------------------

New-Item -ItemType Directory -Force $outDir | Out-Null

Write-Host "==> Building MSI..." -ForegroundColor Cyan
& $wix build (Join-Path $PSScriptRoot "Package.wxs") `
    -d "PublishDir=$stage" -d "AppVersion=$wixVersion" -b $PSScriptRoot `
    -ext WixToolset.UI.wixext `
    -o (Join-Path $outDir "OptiPaie PRO.msi")
if ($LASTEXITCODE -ne 0) { throw "MSI build failed." }

Write-Host "==> Building Setup.exe bootstrapper..." -ForegroundColor Cyan
& $wix build (Join-Path $PSScriptRoot "Bundle.wxs") `
    -d "AppVersion=$wixVersion" -b $PSScriptRoot `
    -ext WixToolset.BootstrapperApplications.wixext `
    -ext WixToolset.Netfx.wixext `
    -o (Join-Path $outDir "OptiPaie PRO Setup.exe")
if ($LASTEXITCODE -ne 0) { throw "Setup.exe build failed." }

# ---------------------------------------------------------------------------
# RELEASE GUARD - Windows 7 SP1 compatibility.
# The retired Velopack installer stub statically imported GetDpiForSystem
# (a Windows 10 1607+ API), so it could not even launch on Windows 7 SP1.
# Refuse to ship any Setup.exe carrying that import so the bug cannot silently
# return. NOTE: we check ONLY "GetDpiForSystem". GetDpiForMonitor / GetDpiForWindow
# legitimately appear in the WiX/thmutil bootstrapper as GetProcAddress-resolved
# strings with a graceful fallback and are Windows 7-safe - do not guard on those.
$setupExe = Join-Path $outDir "OptiPaie PRO Setup.exe"
Write-Host "==> Release guard: checking $setupExe for a GetDpiForSystem import..." -ForegroundColor Cyan
$latin1  = [System.Text.Encoding]::GetEncoding("ISO-8859-1")   # 1 byte -> 1 char (binary-safe scan)
$content = [System.IO.File]::ReadAllText($setupExe, $latin1)
if ($content.IndexOf("GetDpiForSystem", [System.StringComparison]::Ordinal) -ge 0) {
    throw "RELEASE GUARD FAILED: '$setupExe' references GetDpiForSystem - this installer will NOT launch on Windows 7 SP1. Refusing to ship. (This is the exact defect that retired the Velopack rail; see release/README.md and docs/WIN7-VERIFICATION.md.)"
}
Write-Host "    OK - no GetDpiForSystem import; Windows 7 SP1-safe." -ForegroundColor Green
# ---------------------------------------------------------------------------

Remove-Item (Join-Path $outDir "*.wixpdb") -Force -ErrorAction SilentlyContinue
Write-Host "==> Done. Deliverables in $outDir" -ForegroundColor Green
Get-ChildItem $outDir | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,2)}} | Format-Table -AutoSize
