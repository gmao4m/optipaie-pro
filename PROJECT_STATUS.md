# OptiPaie DZ — Project Status

_Last updated: 2026-06-27_

Commercial, **offline** Windows desktop payroll software for Algerian companies.
Single purpose: Algerian payroll (Companies · Employees · Payroll · Archive). Not an
ERP / accounting / HR suite.

---

## 1. Status at a glance

| Area | State |
|---|---|
| Build | **Compiles** (DevExpress 24.2.x); launches without the thread-exception-mode error |
| Payroll engine | ✅ Complete & frozen (approved STEP 5) |
| Data layer (SQLite + Dapper) | ✅ Complete |
| Services / DI | ✅ Complete |
| UI (DevExpress WinForms) | ✅ Functional (V4 polish) |
| Reporting (payslip) | ✅ Builds; layout simple, needs visual polish |
| Tests | ✅ Engine test suite present |
| Runnable EXE produced in this workspace | ❌ No (no DevExpress/.NET build tools here — built locally in Visual Studio) |

---

## 2. Technology & targets

- C# · **.NET Framework 4.8** · WinForms · **DevExpress 24.2.x** · SQLite · Dapper
- Single **x86** build; runs Windows 7 SP1 → 11; 100% offline
- DevExpress version is centralized in `Directory.Build.props` → `<DevExpressVersion>`
  (set it to the exact installed 24.2.x build before restoring).

---

## 3. Solution layout (Clean Architecture, dependencies point inward)

```
OptiPaie.Core          Entities, enums, DTOs, interfaces, primitives (Result, Money, RoundingPolicy)  — depends on nothing
OptiPaie.Common        Logging, config, guards, validation, constants
OptiPaie.PayrollEngine Pure calculation engine (CNAS, IRG, abattement, smoothing, lissage) — refs Core only
OptiPaie.Data          SQLite + Dapper repositories, Unit of Work, migrations, backup provider
OptiPaie.Services      Application services + validators + PayrollService orchestration
OptiPaie.Localization  AR/FR resources + LocalizationService (RTL/LTR)
OptiPaie.Reporting     DevExpress XtraReports payslip + report service (preview + PDF)
OptiPaie.App           WinForms shell, modules, composition root (Startup project, WinExe → OptiPaieDZ.exe)
tests/OptiPaie.Tests   Engine validation + invariants
```

---

## 4. Build & run

1. In `Directory.Build.props`, set `<DevExpressVersion>` to your installed 24.2.x build.
2. Register the DevExpress NuGet feed (see `NuGet.config` / README).
3. Delete any old dev DB so the latest schema regenerates: `%AppData%\OptiPaie DZ\optipaie.db`.
4. `dotnet restore OptiPaie.sln` then build, **or** open `OptiPaie.sln` in VS 2022.
5. Startup project = **OptiPaie.App** (`OutputType=WinExe`, `AssemblyName=OptiPaieDZ`). Press F5.

Runtime data lives under `%AppData%\OptiPaie DZ\` (database, `Logs\`, `Backups\`).

---

## 5. Startup sequence (Program.cs)

`EnableVisualStyles` → `SetCompatibleTextRenderingDefault` → **`SetUnhandledExceptionMode`
(before any control)** → DevExpress settings → Splash → build services → apply
language/skin → attach exception handlers (with file logger) → First-Run Wizard (if no
company) → MainForm.

---

## 6. Implemented features

- **Dashboard:** company/employee counts, current month, archived runs, last backup,
  legal profile, today, DB size, version, recent items, quick actions.
- **Companies / Employees:** full CRUD; employee payroll-element assignment; logo upload.
- **Payroll Playground:** select company/employee/period, auto-load assigned elements,
  add/remove rubriques, calculate (live totals), Explain window, save (nothing auto-saved).
- **Archive:** search, reprint preview, PDF export; immutable history.
- **Settings:** language, theme (Light/Dark/Office), CNAS rate (read-only), backup/restore.
- **First-Run Wizard, License Manager (offline), About, Backup Manager, Developer
  Diagnostic (Ctrl+Alt+D), Splash, status bar, keyboard shortcuts** (F5/Ctrl+N/S/P/F).
- **Bilingual AR/FR** with instant switch + RTL; bilingual payslip with company logo.

---

## 7. Payroll engine (frozen)

Legal basis: CIDTA Art. 104 (LF 2022, in force 2026). Versioned in-engine `LegalProfile`
("DZ-2026"): monthly IRG barème (0/23/27/30/33/35%), exemption ≤ 30 000, abattement 40%
clamped 1 000–1 500, smoothing 30 000–35 000 (137/51, 27925/8), differential lissage.
CNAS 9%/26% + SNMG 24 000 come from the configurable `LegalParameters` table.
Pipeline: Gross → Cotisable → CNAS → Taxable → IRG (brut → abattement → zone) → Lissage → Net.
Every payslip stores the rates + engine/legal versions used (historical reproducibility).

---

## 8. Known issues / remaining work

- **CS0108 warning** (non-blocking): exact hidden member not yet located — needs the full
  `'X' hides 'Y'` line to fix at the root.
- **Validation UX:** Company/Employee forms use inline (dialog) required-field validation;
  the DevExpress `DXValidationProvider` highlight variant was removed pending a verified
  24.2 API.
- **Payslip report:** paper size left at the printer default (A4 on DZ Windows); explicit
  paper-kind set via the verified DevExpress 24.2 API is pending. Layout is functional, not
  yet pixel-polished.
- **Costura single-EXE:** may need temporary disabling on the first DevExpress build.
- **Empty states / theming:** present for key grids; dark theme not visually verified here.

### Pending data confirmations (payroll accuracy)
- Rounding granularity (centime vs whole-dinar) — confirm against a real payslip
  (switch via the `ROUNDING_SCALE` setting → `RoundingPolicy`).
- Validate the **lissage (rappel)** result against one real Algerian rappel payslip.

---

## 9. Not started

- **STEP 7** (post-UI phase) — awaiting approval after Preview Build testing.
- Official declarations (Journal de paie, CNAS DAS, IRG/G50) — data model supports them;
  intentionally out of the v1 UI.
- `Formula` calculation method and disabled/retiree special IRG — intentionally excluded
  from v1 (no approved grammar/formula).
