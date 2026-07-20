# OptiPaie PRO

Commercial **offline payroll software for Algerian companies**, with a complete
cloud **licensing + module-activation platform**, a web **admin console**, and
**automatic updates** via GitHub Releases.

- **Client:** C# · .NET Framework 4.8 · **WPF** · SQLite · QuestPDF (payslips)
- **Cloud (licensing only):** Supabase — licenses, modules, devices, activation
  keys, update metadata. **No payroll, employee, company or business data ever
  leaves the customer's machine.**
- **Updates:** Velopack packaging + **GitHub Releases** as the feed
- **Admin:** self-contained web app (`admin/`)

The payroll engine (CNAS, Base Imposable, IRG, Abattement, Lissage, Net) is a
validated black box and is never modified by the licensing/update layers.

---

## Repository layout

| Path | What |
|------|------|
| `src/` | .NET solution: `OptiPaie.Desktop` (WPF client), `OptiPaie.Admin` (WPF admin desktop) + Core/Common/Data/Services/PayrollEngine/Localization |
| `admin/` | Web admin console (`index.html`) — same features as the desktop admin, hostable anywhere |
| `backend/` | Supabase: `setup.sql` (tables/RLS/functions), Edge Functions (`activate`, `validate`, `activate-module`), `CONNECT.md` |
| `installer/` | WiX MSI installer (one-time, offline installs) |
| `release/` | Velopack auto-update pipeline (`pack.ps1`) + docs |
| `.github/workflows/` | `release.yml` — build + publish a GitHub Release on a version tag |
| `build/` | Product icon |

## The two applications

### 1. Desktop client (`src/OptiPaie.Desktop`)
Runs fully **offline** except for license activation/validation, module
synchronization and update checking. First launch shows an **Activation window**
(activate a key, or start a **30-day trial**). Modules the license doesn't include
appear locked (🔒) with a premium page; a customer unlocks one anytime via
**Settings → Activate Module** (single-use key) — no reinstall. The local license
cache is DPAPI-encrypted; tokens are Ed25519-signed and verified offline.

### 2. Admin console — two interchangeable forms
Both talk to Supabase over REST with **owner login** and the **publishable key
only** (the service-role key is never used). Same feature set:
**Dashboard, Licenses** (search/filter/sort/pagination + create/edit/enable/disable/
extend/reset-device/delete), **Modules** (ON/OFF + activation keys generate/revoke/
history), **Devices**, **Updates** (publish/mandatory/set-latest/delete),
**Reports**, **Audit Log**, **Bulk generator** (10–1000 + CSV export).
- **`OptiPaie.Admin`** — native **WPF desktop** admin (`OptiPaie PRO Admin.exe`).
- **`admin/index.html`** — single-file **web** admin; host free on Cloudflare Pages /
  Vercel / Netlify to manage from any browser.

## Build & run (developer)
```powershell
dotnet build src/OptiPaie.Desktop/OptiPaie.Desktop.csproj -c Release
```
Requires the .NET SDK + .NET Framework 4.8 targeting pack. (The DevExpress-based
`OptiPaie.App`/`OptiPaie.Reporting` projects are legacy; the shipping client is
`OptiPaie.Desktop` and does not use DevExpress.)

## Installer & release
- **One-time MSI:** `installer/build-installer.ps1` → `OptiPaie PRO Setup.exe` + MSI.
- **Auto-updating release:** `release/pack.ps1 -Version X.Y.Z` → Velopack Setup.exe +
  full/delta packages + RELEASES. Or push a `vX.Y.Z` tag and the GitHub Action
  publishes the release automatically.

## Cloud setup (Supabase)
See `backend/CONNECT.md`: run `backend/supabase/setup.sql`, deploy the three Edge
Functions (`--no-verify-jwt`), set the signing secret. The public key is embedded in
the client; the private key stays a Supabase secret.

## Tests
Automated tests cover the licensing state machine, trial, device identity, encrypted
cache, module activation (valid/invalid/reused/expired/revoked/offline/signature),
and updates (version comparison, downgrade guard, optional/mandatory, network
failure, GitHub release parsing) — plus the NUnit payroll-engine suite in `tests/`.

## Steps that require YOUR credentials (documented, not automatable)
1. **GitHub** — create the repo and `git push`; the release workflow then runs on tag
   pushes using the automatic `GITHUB_TOKEN`.
2. **Supabase** — run `setup.sql` and `supabase functions deploy` (your CLI login).
3. **Update feed** — set `Update.GitHubRepo` (App.config) to `owner/repo`.
4. **Code signing** — provide a certificate and pass `--signParams` to `vpk pack`
   (and sign the MSI) when ready.

© 2026 OptiPaie. Tous droits réservés.
