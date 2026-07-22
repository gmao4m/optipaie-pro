# Module 1 — Présence (Attendance Matrix)

Premium module, module key `attendance`. Redesigned from a day-sheet CRUD into a
professional, Excel-like **Attendance Matrix**: one screen to run a whole company's
month. Employees are rows; every day of the month is a colour-coded column. Pick a status
"brush", click cells — it auto-saves. Payroll consumes the same records.

---

## 1. The Matrix screen

**Filters (toolbar):** Company · Department · Month · Year · status quick-filter · instant
search. **KPI strip:** attendance rate, absence rate, late rate, working days, present
today, on leave, on mission, head-count.

**Grid:** frozen identity columns — ✓ (select), N°, Employé, Département, Poste — then one
column per day of the month. Columns and rows are **virtualised** (recycling), so the grid
stays responsive for large head-counts. The identity columns are frozen while the days
scroll horizontally.

### Cell colours (understand the month at a glance)

| Colour | Status | Payroll effect |
|---|---|---|
| 🟩 Green | Présent (P) | worked, paid |
| 🟥 Red | Absent (A) | reduces worked days (deducted) |
| 🟧 Orange | Retard (R) | worked, paid (flagged late) |
| 🟦 Blue | Mission (M) | worked, paid |
| 🟪 Purple | Congé payé (C) | paid, not worked |
| 🟨 Yellow | Jour férié (F) | paid |
| ⬜ Gray | Repos / week-end (W) | not worked — Friday/Saturday auto-gray |

## 2. Fast data entry (no Save button)

- **Paint brush:** pick a status in the palette, then **click any cell** to set it —
  saved immediately (`SetDayStatus`).
- **Fill a whole day:** click a **day header** → the status is applied to every displayed
  employee for that day (`SetDayStatusBulk`).
- **Bulk over selected employees:** check rows, click **“Appliquer au mois (sélection)”** →
  the status fills every working day of the month (weekends skipped).
- **Select all / clear** helpers; the status quick-filter and search narrow the visible
  rows instantly.

Every change is auto-saved and the KPIs update live. Future days are read-only (the matrix
never records the future). Past months stay fully accessible through the month/year
selectors — the archive is automatic (records are simply kept per month).

## 3. Employee history

**Double-click an employee's name** → a dialog with the month **calendar** (same colours),
the month **statistics** (present/absent/late/leave/holiday, worked & overtime hours,
late minutes) and the **late / absence day lists**, with month navigation.

## 4. Payroll integration (engine untouched)

The matrix writes the same `AttendanceRecords` payroll already reads (see
`PayrollViewModel`): absences reduce `WorkedDays`; Present/Late/Mission count as worked
days; paid leave and holidays are paid, not deducted. A worked status entered from the
matrix counts as one standard day (`StandardHours`); precise per-day hour entry remains
available. The payroll engine, licensing and module-activation systems are unchanged.

## 5. Business rules & data

All rules live in `OptiPaie.Services/AttendanceService.cs`:
- one record per employee per day (upsert; partial unique index);
- status-only entry (`SetDayStatus` / `SetDayStatusBulk`) needs no times — a worked status
  is one standard day, non-worked statuses carry no hours;
- **Mission** (status 7) is a worked, paid day and never an absence;
- monthly summaries and the payroll feed treat Present/Late/Mission as present.

Schema (additive migrations):
- `0019_EmployeeDepartment.sql` — adds `Employees.Department` (filter/column source).
- `0020_AttendanceMissionStatus.sql` — widens the `AttendanceRecords.Status` CHECK to
  include 7 (Mission) via a safe table rebuild; data preserved, indexes recreated.

## 6. Files

| Layer | File |
|---|---|
| Core | `Enums/AttendanceStatus.cs` (+Mission), `Entities/Employee.cs` (+Department), `Dtos/AttendanceSummary.cs` (+`AttendanceDayStatus`, `AttendanceKpis`) |
| Data | `Sql/Migrations/0019_*.sql`, `0020_*.sql`, `Repositories/EmployeeRepository.cs` (Department), `AttendanceRepository.cs` |
| Services | `AttendanceService.cs` (status-only + bulk + month fetch + Mission) |
| Desktop VMs | `ViewModels/Attendance/AttendanceMatrixViewModel.cs`, `MatrixRowViewModel`, `MatrixCellViewModel`, `StatusBrushViewModel`, `AttendanceAppearance`, `AttendanceEmployeeDetailViewModel` |
| Desktop Views | `Views/AttendanceMatrixView.xaml(.cs)` (dynamic day columns), `AttendanceEmployeeDetailWindow.xaml` |
| Reuse | `AttendanceMonthWindow` (reports/PDF/CSV), `AttendanceSettingsWindow` |

## 7. Tests & verification

`AttendanceMatrixServiceTests` — 9 integration tests on real SQLite (also exercise the
0020 table rebuild): status-only Present needs no times and counts a standard day; Mission
persists and is worked-not-absent; Absent feeds the count; overwrite = no duplicate; future
skipped; bulk writes atomically; `GetCompanyMonth`/`GetEmployeeMonth` scope by month;
Department round-trips.

The generated day-cell / name / checkbox `XamlReader` templates were parsed and validated.
Full suite **1340/1340**; `OptiPaie.Desktop` builds **0 errors / 0 warnings**.

## 8. Honest scope notes (follow-ups)

- **Keyboard**: arrow-key navigation is native to the grid; full Excel-style
  copy/paste/fill-down-right and 2-D cell multi-selection are **not** implemented — the
  paint-brush + day-header + row-bulk cover the same speed. Planned enhancement.
- **Export**: reports export PDF and **CSV** (opens in Excel); native `.xlsx` is a
  follow-up.
- **Dark mode**: not yet — the app currently uses static-resource theming; a live
  light/dark toggle needs a theming pass.
- **Public holidays**: weekends (Fri/Sat) auto-gray; national holidays are paintable
  (yellow) but not yet auto-filled from a calendar.
- The matrix could not be click-tested in this environment; it is verified by a clean
  build, the parsed runtime templates, and the service test suite.
