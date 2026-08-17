# RETIRED — Velopack auto-update rail (do not resurrect)

**This rail was removed on 2026-08-17. Do not rebuild it.**

## Why it was killed

The Velopack installer/updater stub (`OptiPaiePRO-win-Setup.exe`, produced by the old
`release/pack.ps1` via the `vpk` CLI) is a native binary that **statically imports
`GetDpiForSystem`** — a Windows 10 (1607+) API. On **Windows 7 SP1** the loader cannot
resolve that entry point in `user32.dll`, so the installer **fails to launch before any
application code runs**. A real client on Windows 7 hit exactly this.

The .NET Framework 4.8 application itself is fully Windows 7 SP1-compatible; only the
Velopack wrapper was not. Shipping it broke Windows 7 customers.

## What replaced it

- **Installer:** the WiX Burn bundle → `installer/build-installer.ps1` →
  `installer/output/OptiPaie-PRO-Setup.exe`. Verified to contain **no `GetDpiForSystem`
  import**; runs on Windows 7 SP1. `installer/build-installer.ps1` now **fails the build**
  if the produced Setup.exe contains that import.
- **Auto-update:** the `version.json` manifest on `main`
  (`https://raw.githubusercontent.com/gmao4m/optipaie-pro/main/version.json`), served over
  HTTPS and handled in-app by `VersionJsonReleaseChannel` — no native stub, Windows 7-safe.
  See `docs/release-*` and `docs/WIN7-VERIFICATION.md`.

## What was removed

- `release/pack.ps1` (deleted).
- `Velopack` NuGet package + `VelopackReleaseChannel.cs` + the `VelopackApp.Build().Run()`
  startup hook + `Update.FeedUrl` config (deleted from the app).
- Local Velopack artifacts under `release/output/` (purged).

If you need to migrate a customer still on the old Velopack build (Windows 7 or otherwise),
see **`docs/CLIENT-WIN7-MIGRATION.md`**: uninstall the old app, install the WiX Setup.exe;
their data in `%AppData%\OptiPaie DZ` is preserved.
