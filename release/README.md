# OptiPaie PRO — Auto-Update (Velopack)

Complete self-update, Chrome/Discord/VS-Code style: the customer installs once and
the app updates itself silently thereafter. Built on **Velopack 1.2.0**.

## How it fits together
- **Feed** = the contents of `release/output` (RELEASES manifest + `.nupkg`
  packages), hosted on any static host — e.g. a **public Supabase Storage bucket**.
- **App** reads the feed to find the latest version and download/apply updates.
  The **mandatory flag + release notes** come from the Supabase `updates` table
  (managed in the Admin Panel) — matched by version.
- **Config** (`App.config`): `Update.FeedUrl` points at the hosted feed. Blank =
  updates disabled (app still runs normally).

## Build a release
```powershell
powershell -ExecutionPolicy Bypass -File release\pack.ps1 -Version 1.0.0
```
Produces in `release/output`:
| File | Purpose |
|------|---------|
| `OptiPaiePRO-win-Setup.exe` | The installer the customer runs **once**. |
| `OptiPaiePRO-<ver>-full.nupkg` | Full package. |
| `OptiPaiePRO-<ver>-delta.nupkg` | Delta vs the previous release (auto — created when a prior release is in the output folder). |
| `RELEASES` / `releases.win.json` | Manifest with **SHA checksums** (integrity). |

To publish a new version: bump `-Version`, run again (the delta is generated
automatically), and upload the **entire** `release/output` folder to your feed host.

## Publish flow (each new version)
1. `pack.ps1 -Version X.Y.Z` → builds full + delta + Setup + RELEASES.
2. Upload `release/output/*` to the feed host (overwrite RELEASES).
3. In the **Admin Panel → Mises à jour**, create the version row (version, release
   notes, **mandatory yes/no**). That row drives the in-app dialog.

Installed customers pick it up at next startup (and within 24h while running).

## Customer experience
- First install: run `OptiPaiePRO-win-Setup.exe` → installs to
  `%LocalAppData%\OptiPaiePRO` (per-user, no admin), creates Desktop + Start Menu
  shortcuts, launches.
- Later: on startup (and every 24h) the app checks the feed. If a newer version
  exists, a professional dialog shows current vs latest version + release notes
  with **Mettre à jour maintenant / Plus tard**.
- **Optional** update → "Plus tard" continues normally.
- **Mandatory** update → no "Plus tard"; dismissing it closes the app until updated.
- Download shows progress, Velopack verifies package integrity, installs, and
  **restarts into the new version** — no manual installer.

## Data safety (guaranteed)
Updates replace only the app binaries under `%LocalAppData%\OptiPaiePRO`. The
SQLite database, employees, companies, payroll, archives, settings, **license
cache and activated modules** live in `%AppData%\OptiPaie DZ` and are never touched.

## Security
- **Package integrity**: Velopack verifies each package's checksum from RELEASES.
- **Downgrade prevention**: only a strictly-newer version is offered (`UpdatePolicy`
  + `AppVersion`, unit-tested).
- **Ready for code-signing**: pass a signing command to `vpk pack` when you have a
  certificate:
  ```
  vpk pack ... --signParams "/a /fd sha256 /tr http://timestamp.digicert.com /td sha256"
  ```
  Sign the app before packing; sign the Setup bundle after.

## Note: two installer paths in this repo
- **Velopack** (`release/`) — the **auto-updating** installer. Use this for
  customers who should receive automatic updates (recommended).
- **WiX MSI** (`installer/`) — the earlier one-time MSI/Setup.exe. Still valid for
  managed/offline deployments, but it does **not** auto-update.

## Tested here
- Build + Velopack `pack` verified end-to-end: full + **delta** + Setup.exe +
  RELEASES + checksums all generated.
- Update **logic** unit-tested (21 tests): version comparison, downgrade guard,
  no-update / optional / mandatory, network interruption, download/verification
  failure, successful install + restart, progress, graceful "not supported".
- App builds Release **0/0** and launches with the Velopack hook.

**Requires manual end-to-end verification on your side** (only possible with a
hosted feed + two published versions): install `Setup.exe`, publish a higher
version to the feed, relaunch → confirm the dialog, download, restart, and that
your data survives. Everything is wired for it.
