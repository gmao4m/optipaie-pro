# OptiPaie PRO — Installer

Professional Windows installer for OptiPaie PRO, built with **WiX v5**.

> Inno Setup was the stated preference, but it is not installed on this machine,
> whereas the WiX v5 toolchain is available and can produce the full deliverable
> (a themed `Setup.exe` bootstrapper + an MSI wizard) end-to-end. WiX is the
> industry standard for Windows installers, so the result is fully professional.

## Deliverables (in `output/`)

| File | What it does |
|------|--------------|
| **`OptiPaie PRO Setup.exe`** | Single-file installer. Checks for **.NET Framework 4.8** and installs it if missing, then installs the app. The customer double-clicks it — nothing else. |
| **`OptiPaie PRO.msi`** | The MSI wizard: Welcome → License → **Install location** → Install → Finish. Use directly if you prefer a location-selection wizard. |

Both create a **Desktop shortcut** and a **Start Menu shortcut** ("OptiPaie PRO"),
register an **uninstaller** in Add/Remove Programs, and use the product icon.

## Customer experience
1. Double-click **`OptiPaie PRO Setup.exe`**.
2. Accept the licence → Install (Windows shows the standard UAC prompt).
3. .NET 4.8 is installed only if absent (already present on Windows 10 1903+/11).
4. App is installed to `C:\Program Files (x86)\OptiPaie PRO`.
5. Desktop + Start Menu shortcuts are created; the app is ready to launch.

## Data safety (verified)
The SQLite database, backups and license cache live in
`%AppData%\OptiPaie DZ` — **outside** the install folder. Install, upgrade and
uninstall only touch program files, so payroll history, employees, companies,
payslips, settings and the license cache are **never** deleted.

Upgrades are handled by `MajorUpgrade` (same `UpgradeCode`): a newer version
cleanly replaces the binaries and keeps all data.

## First run (automatic)
On first launch the app creates its folders, initialises the SQLite database,
runs all migrations and seeds the default payroll rubrics — verified by a launch
test against an isolated data directory. The customer configures nothing.

## Rebuilding
```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```
This builds the Release payload, then the MSI and `Setup.exe`.

## Digital signing (later)
The package is ready for Authenticode signing. Once you have a code-signing
certificate, sign the app exe, the MSI and the bundle:
```powershell
signtool sign /fd SHA256 /a /tr http://timestamp.digicert.com /td SHA256 "OptiPaie PRO.exe"
signtool sign /fd SHA256 /a /tr http://timestamp.digicert.com /td SHA256 "output\OptiPaie PRO.msi"
# Bundles must be signed with WiX's "insignia" step before signing the exe.
```

## Notes / current state
- The base payroll product (Companies, Employees, Payroll engine, Rubriques,
  Archive, PDF/QuestPDF, Settings, Backup/Restore, FR/AR + RTL, SQLite) is built
  clean in **Release (0 errors, 0 warnings)** and launches correctly.
- Premium HR modules show their **locked upsell pages** until unlocked by a
  license. The in-app **activation screen** (entering a key to unlock online) is
  the next licensing step and is not yet wired to the UI in this build; the
  licensing engine, gate and premium pages are in place.
- The licensing backend endpoints (Supabase) and the embedded Ed25519 public key
  are placeholders in config until you complete the cloud setup (see
  `backend/README.md`); the app runs fully offline without them.
