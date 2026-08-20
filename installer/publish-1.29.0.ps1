<#
    OptiPaie PRO - DIFFUSION of v1.29.0 (GitHub release + version.json flip).

    RUN THIS ONLY AFTER the manual test is validated and the go-ahead is given.
    It is the step that actually pushes the update to every client. Until it runs,
    clients stay on 1.27.0.

    One command:
        powershell -ExecutionPolicy Bypass -File installer\publish-1.29.0.ps1

    What it does, in order (stops on the first failure; safe to re-run):
      0. Preconditions: on main, clean tree, HEAD pushed, tag not already released,
         built Setup.exe really is 1.29.0.
      1. Copy the freshly-built "OptiPaie PRO Setup.exe" to the load-bearing asset
         name "OptiPaie-PRO-Setup.exe".
      2. Create the GitHub release v1.29.0 (owner gmao4m), notes = the Arabic
         changelog, target = current HEAD, marked latest.
      3. Verify the PUBLIC asset URL unauthenticated (HTTP 200 + SHA256 == local)
         BEFORE touching version.json - a dead/mismatched link breaks every client.
      4. Only then: flip version.json to 1.29.0 (from installer\version.1.29.0.json)
         and push it to main.
      5. Confirm main serves 1.29.0 (bypassing the raw CDN cache).
#>
$ErrorActionPreference = "Stop"

$ver        = "1.29.0"
$tag        = "v$ver"
$repo       = "gmao4m/optipaie-pro"
$root       = Split-Path -Parent $PSScriptRoot
$builtSetup = Join-Path $PSScriptRoot "output\OptiPaie PRO Setup.exe"
$assetSetup = Join-Path $PSScriptRoot "output\OptiPaie-PRO-Setup.exe"
$notesFile  = Join-Path $root "docs\CHANGELOG-$ver-ar.md"
$versionTpl = Join-Path $PSScriptRoot "version.$ver.json"
$versionOut = Join-Path $root "version.json"
$assetUrl   = "https://github.com/$repo/releases/download/$tag/OptiPaie-PRO-Setup.exe"

Write-Host "==> Publishing OptiPaie PRO $tag to $repo" -ForegroundColor Cyan

# --- 0. Preconditions -------------------------------------------------------
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne "main") { throw "Not on main (on '$branch'). Diffusion runs from main only." }

if (git status --porcelain) { throw "Working tree not clean. Commit or stash before diffusing." }

try { git fetch origin main --quiet } catch { Write-Host "   (fetch blip ignored)" -ForegroundColor Yellow }
$head   = (git rev-parse HEAD).Trim()
$remote = (git rev-parse origin/main).Trim()
if ($head -ne $remote) { throw "HEAD ($head) != origin/main ($remote). Push your commits first." }

foreach ($f in @($builtSetup, $notesFile, $versionTpl)) {
    if (-not (Test-Path $f)) { throw "Missing required file: $f" }
}

$pv = (Get-Item $builtSetup).VersionInfo.ProductVersion
if ($pv -notlike "$ver*") { throw "Setup.exe ProductVersion '$pv' does not match $ver. Rebuild the installer." }

$env:GH_TOKEN = (gh auth token --hostname github.com --user gmao4m).Trim()
if (-not $env:GH_TOKEN) { throw "No gmao4m gh token available (gh auth token --user gmao4m)." }

gh release view $tag --repo $repo 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) { throw "Release $tag already exists. Aborting to avoid overwriting a live release." }

# --- 1. Asset name (hyphenated, load-bearing) -------------------------------
Write-Host "==> Copying Setup.exe to the release asset name..." -ForegroundColor Cyan
Copy-Item $builtSetup $assetSetup -Force
$localHash = (Get-FileHash $assetSetup -Algorithm SHA256).Hash
Write-Host "    local SHA256 = $localHash"

# --- 2. Create the GitHub release (retry on transient network blips) --------
Write-Host "==> Creating GitHub release $tag (target $head)..." -ForegroundColor Cyan
$ok = $false
for ($i = 1; $i -le 4; $i++) {
    gh release create $tag "$assetSetup" --repo $repo --target $head --title $tag --notes-file "$notesFile" --latest
    if ($LASTEXITCODE -eq 0) { $ok = $true; break }
    Write-Host "   release attempt $i failed; retrying..." -ForegroundColor Yellow
    Start-Sleep -Seconds 5
}
if (-not $ok) { throw "gh release create failed after retries." }

# --- 3. Verify the asset like a client (UNAUTHENTICATED) --------------------
Write-Host "==> Verifying the public asset URL (unauthenticated)..." -ForegroundColor Cyan
$tmp = Join-Path $env:TEMP "optipaie_1290_verify.exe"
$verified = $false
for ($i = 1; $i -le 6; $i++) {
    if (Test-Path $tmp) { Remove-Item $tmp -Force }
    curl.exe -sL -o "$tmp" "$assetUrl"
    if ((Test-Path $tmp) -and ((Get-Item $tmp).Length -gt 1MB)) {
        $dlHash = (Get-FileHash $tmp -Algorithm SHA256).Hash
        if ($dlHash -eq $localHash) { $verified = $true; break }
        Write-Host "   downloaded hash mismatch (asset still propagating?)..." -ForegroundColor Yellow
    } else {
        Write-Host "   asset not reachable yet (attempt $i)..." -ForegroundColor Yellow
    }
    Start-Sleep -Seconds 6
}
if (-not $verified) { throw "Public asset did NOT verify (200 + SHA256 match). version.json left UNTOUCHED." }
Write-Host "    OK - public asset matches the built installer byte-for-byte." -ForegroundColor Green

# --- 4. Flip version.json to 1.29.0 and push --------------------------------
Write-Host "==> Updating version.json -> $ver and pushing..." -ForegroundColor Cyan
Copy-Item $versionTpl $versionOut -Force
git add $versionOut
git commit -m "chore(release): version.json -> $ver (diffusion) [skip ci]"
$pushed = $false
for ($i = 1; $i -le 4; $i++) {
    git push origin main
    if ($LASTEXITCODE -eq 0) { $pushed = $true; break }
    Write-Host "   push attempt $i failed; retrying..." -ForegroundColor Yellow
    Start-Sleep -Seconds 5
}
if (-not $pushed) { throw "version.json committed locally but push FAILED - retry 'git push origin main' manually." }

# --- 5. Confirm the manifest on main (bypass raw CDN cache) -----------------
Write-Host "==> Confirming version.json on main (non-CDN)..." -ForegroundColor Cyan
$manifest = gh api -H "Accept: application/vnd.github.raw" "repos/$repo/contents/version.json?ref=main"
if ($manifest -match '"latest_version"\s*:\s*"1\.29\.0"') {
    Write-Host "    OK - main serves latest_version 1.29.0." -ForegroundColor Green
} else {
    Write-Host "   NOTE: main manifest not showing 1.29.0 yet (raw.githubusercontent.com CDN lag up to ~5 min)." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "==> DONE. $tag is LIVE. Clients below 1.29.0 will be offered the update." -ForegroundColor Green
Write-Host "    Release : https://github.com/$repo/releases/tag/$tag"
Write-Host "    Asset   : $assetUrl"
