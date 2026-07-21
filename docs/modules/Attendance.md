# Module 1 — Présence (Attendance)

Premium module, module key `attendance`. First module of the HR ecosystem built on top
of the payroll core. It is **not** a standalone application: it reads the same
`Employees` and `Companies` tables as payroll and feeds its monthly totals straight
back into the payslip calculation.

---

## 1. What it does

| Capability | Where |
|---|---|
| Daily attendance sheet for a whole company | `Présence` screen |
| Status per employee (Présent / Absent / Retard / Congé / Jour férié / Repos) | day grid |
| Check-in / check-out, worked hours, late minutes, overtime | computed by the service |
| "Marquer tous présents" bulk action | day grid |
| Live KPIs (présents, absents, retards, heures supp.) | KPI strip |
| Monthly synthesis per employee | `Synthèse du mois` dialog |
| Export PDF (A4) and CSV (Excel-ready, UTF-8 BOM) | synthesis dialog |
| Module parameters (start time, standard hours, tolerance) | `Paramètres` dialog |
| Automatic payroll feed | `Établir une paie` screen |

## 2. Business rules (single source of truth)

All rules live in `OptiPaie.Services/AttendanceService.cs`. No screen computes anything
itself, so the grid, the report and payroll can never disagree.

- **One record per employee per day.** Saving the same day twice updates the existing
  row. Enforced twice: by the service (upsert) and by the database
  (`UX_Attendance_Employee_Date` unique index).
- **Worked hours** = check-out − check-in, rounded to 2 decimals (away from zero).
- **Late minutes** = arrival − (standard start + tolerance), never negative, rounded up.
- **Overtime** = worked hours above the standard day, 0 when at or below.
- **Status normalisation**: a worked day with lateness is stored as `Retard`; without
  lateness as `Présent`. Non-worked statuses (Absent / Congé / Férié / Repos) clear the
  times and zero the hours, lateness and overtime.
- **Validation**: employee must exist, date is required and cannot be in the future,
  times must parse as `HH:mm`, check-out must not precede check-in, and a worked day
  requires a check-in.

### Parameters

Stored in the shared `AppSettings` table, so they survive updates and backups.

| Key | Default | Meaning |
|---|---|---|
| `Attendance.StandardStart` | `08:00` | Official start of the working day |
| `Attendance.StandardHours` | `8` | Standard hours per day (overtime threshold) |
| `Attendance.LateToleranceMinutes` | `10` | Grace period before an arrival counts as late |

## 3. Data model

Migration `src/OptiPaie.Data/Sql/Migrations/0008_Attendance.sql` — **additive only**, no
existing table is altered.

```
AttendanceRecords
  Id            INTEGER PK
  EmployeeId    INTEGER  → FK Employees(Id)      -- the SHARED employee table
  WorkDate      TEXT (date)
  Status        INTEGER  (1 Present, 2 Absent, 3 Late, 4 Leave, 5 Holiday, 6 Rest)
  CheckIn       TEXT "HH:mm"      CheckOut TEXT "HH:mm"
  WorkedHours   TEXT (invariant decimal)   LateMinutes INTEGER   OvertimeHours TEXT
  Notes         TEXT
  CreatedAtUtc / UpdatedAtUtc / IsDeleted
  UNIQUE INDEX UX_Attendance_Employee_Date (EmployeeId, WorkDate) WHERE IsDeleted = 0
```

Migration `0009_AttendanceSoftDeleteIndex.sql` makes that index **partial**. 0008 made
the pair unique across all rows including soft-deleted ones, so deleting a day and
recording it again hit the constraint (caught by `Delete_ThenRecordTheSameDayAgain_Succeeds`).
The rule enforced is "one *live* record per employee and day"; deleted rows remain as history.

There is **no** company column: a company's attendance is obtained by joining
`Employees`. Moving an employee between companies therefore needs no attendance
migration, and company data exists in exactly one place.

## 4. Payroll integration (engine untouched)

`PayrollViewModel.BuildRequest()` enriches the **inputs** of the payroll request; the
payroll engine, its formulas, its rates and the legal rules are not modified.

When — and only when — the module is licensed **and** the period has recorded days:

- `WorkedDays` = days in month − absences
- `WorkedHours` = the month's recorded hours
- a badge in the worksheet states what attendance contributed, so the figures are never
  a black box.

With no records (or a locked module) the request is byte-for-byte the previous one, so
existing payslips are unaffected. There is no import/export step: the data is already
shared.

## 5. UI / navigation

- Locked: the nav entry shows 🔒 and opens the premium (upsell) page.
- Licensed: `ShellViewModel.ResolveModule` routes `attendance` to `AttendanceViewModel`
  → `AttendanceView`. No reinstall, no migration — activation alone flips it.

## 6. Files

| Layer | File |
|---|---|
| Core | `Enums/AttendanceStatus.cs`, `Entities/AttendanceRecord.cs`, `Dtos/AttendanceSummary.cs` |
| Core | `Interfaces/Repositories/IAttendanceRepository.cs`, `Interfaces/Services/IAttendanceService.cs` |
| Data | `Sql/Migrations/0008_Attendance.sql`, `0009_AttendanceSoftDeleteIndex.sql`, `Repositories/AttendanceRepository.cs`, `Context/UnitOfWork.cs` |
| Services | `AttendanceService.cs` |
| Desktop | `ViewModels/AttendanceViewModel.cs`, `AttendanceMonthViewModel.cs`, `AttendanceSettingsViewModel.cs` |
| Desktop | `Views/AttendanceView.xaml`, `AttendanceMonthWindow.xaml`, `AttendanceSettingsWindow.xaml` |
| Desktop | `Documents/AttendanceReportDocument.cs` (QuestPDF A4 report) |
| Tests | `tests/OptiPaie.Tests/AttendanceServiceTests.cs` |

## 7. Tests

`AttendanceServiceTests` — 17 integration tests against a **real SQLite file** with the
real migrations, repositories and service:

- worked hours + overtime, lateness within/beyond tolerance, absent day carries no hours
- same day saved twice updates instead of duplicating; `SaveMany` writes a whole day
- company scoping goes through the shared `Employees` table
- monthly summary aggregation, month isolation, per-company summary, empty month
- delete removes the day from the summary, and the day can then be recorded again
- settings round-trip and immediately change the calculation
- rejections: invalid time, future date, unknown employee

Status: **17/17 passing**, full suite **1193/1193 passing**. `OptiPaie.Desktop` builds
0 errors / 0 warnings (the solution's 5 remaining warnings are pre-existing, in the
legacy WinForms `OptiPaie.App` / `OptiPaie.Reporting` projects that are not shipped).
