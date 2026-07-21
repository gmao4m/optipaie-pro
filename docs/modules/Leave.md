# Module 2 — Congés (Leave)

Premium module, module key `leave`. Second module of the HR ecosystem. It is the
module that proves the "no manual synchronisation" promise: **approving a leave request
writes the days straight into the Attendance module**, which is what payroll already
reads. There is no import, no export, and no second copy of a day.

---

## 1. What it does

| Capability | Where |
|---|---|
| Leave requests of a company for a year | `Congés` screen |
| Create / edit a request with live day count | `Nouvelle demande` dialog |
| Approve · Refuse · Cancel · Delete | action bar |
| Annual-leave balance of the selected employee | KPI strip |
| Balances of the whole team, PDF (A4) + CSV export | `Soldes de l'équipe` dialog |
| Module parameters (accrual, cap, rest days) | `Paramètres` dialog |
| Automatic attendance + payroll feed | on approval |

## 2. Business rules (single source of truth)

All rules live in `OptiPaie.Services/LeaveService.cs`.

- **Day counting** excludes the Algerian weekly rest — **Friday and Saturday** — unless
  the parameter is turned off. A period made only of rest days is refused.
- **No overlap**: two *live* requests (pending or approved) of one employee may never
  cover the same day. Rejected and cancelled ones do not block.
- **Annual entitlement** = 2,5 days per month worked in the year, capped at 30
  (loi 90-11 art. 41). Pro-rated from the hire date and, when applicable, the exit
  date — both read from the shared employee record.
- **Balance** = entitlement − approved annual days. Pending days are shown separately
  and never silently deducted. Sick / maternity / special leave never touch the annual
  balance; unpaid days are tracked on their own line.
- **Lifecycle**: a request may only be edited while `Pending`; only a `Pending` request
  can be approved or refused; only an `Approved` one can be cancelled; a rejected one
  cannot be cancelled.
- **Cross-year requests** count, for a given year, only the days that fall inside it.

### Parameters

Stored in the shared `AppSettings` table.

| Key | Default | Meaning |
|---|---|---|
| `Leave.DaysPerMonth` | `2.5` | Days earned per month worked |
| `Leave.AnnualCap` | `30` | Yearly cap on annual leave |
| `Leave.ExcludeRestDays` | `1` | Exclude Friday/Saturday from the count |

## 3. Cross-module synchronisation (the core of the ecosystem)

On **approval**, one attendance row is written per leave day:

| Leave type | Attendance status | Effect on payroll |
|---|---|---|
| Sans solde (`Unpaid`) | `Absent` | worked days reduced → salary deducted |
| Annuel / Maladie / Maternité / Exceptionnel | `Congé` | paid, worked days unchanged |

Rows written by this module are tagged `[Congé] <type>` in their note. On **cancel** or
**delete** only those tagged rows are removed — a day the user recorded by hand in the
Attendance screen is never touched. Rest days are skipped, so no attendance row is
created for a Friday or a Saturday.

Because payroll already consumes the attendance summary (see
[Attendance](Attendance.md) §4), unpaid leave reaches the payslip with **zero**
additional code in the payroll engine, which remains untouched.

## 4. Data model

Migration `src/OptiPaie.Data/Sql/Migrations/0010_Leave.sql` — additive only.

```
LeaveRequests
  Id           INTEGER PK
  EmployeeId   INTEGER  → FK Employees(Id)     -- the SHARED employee table
  Type         INTEGER  (1 Annual, 2 Sick, 3 Unpaid, 4 Maternity, 5 Special)
  Status       INTEGER  (1 Pending, 2 Approved, 3 Rejected, 4 Cancelled)
  StartDate / EndDate  TEXT (date)   CHECK EndDate >= StartDate
  Days         TEXT (invariant decimal)   -- derived, never typed in
  Reason / DecisionNote / DecidedAtUtc
  CreatedAtUtc / UpdatedAtUtc / IsDeleted
```

No company column: a company's requests come from joining `Employees`.

### A shared-date bug this module exposed

`System.Data.SQLite` renders the same calendar day differently depending on the
`DateTime.Kind` it is handed — `2025-06-01 00:00:00` for `Utc`, `2025-06-01 00:00:00Z`
for `Unspecified`. The Leave module writes attendance days from dates that came *out of*
the database (Utc) while the Attendance screen wrote them from `new DateTime(...)`
(Unspecified), so `WHERE WorkDate = @day` silently found nothing across modules.

Fixed at the boundary: `OptiPaie.Data/Context/SqliteDate.cs` gives every day exactly one
representation, both repositories bind through it, and migration
`0011_NormaliseAttendanceDates.sql` normalises rows written the old way. Covered by
`ApprovedLeaveAndManualPointage_ShareOneDayRepresentation`.

## 5. Files

| Layer | File |
|---|---|
| Core | `Enums/LeaveType.cs`, `Enums/LeaveStatus.cs`, `Entities/LeaveRequest.cs`, `Dtos/LeaveBalance.cs` |
| Core | `Interfaces/Repositories/ILeaveRepository.cs`, `Interfaces/Services/ILeaveService.cs` |
| Data | `Sql/Migrations/0010_Leave.sql`, `0011_NormaliseAttendanceDates.sql`, `Repositories/LeaveRepository.cs`, `Context/SqliteDate.cs` |
| Services | `LeaveService.cs` |
| Desktop | `ViewModels/LeaveViewModel.cs`, `LeaveEditViewModel.cs`, `LeaveBalancesViewModel.cs`, `LeaveSettingsViewModel.cs` |
| Desktop | `Views/LeaveView.xaml`, `LeaveEditWindow.xaml`, `LeaveBalancesWindow.xaml`, `LeaveSettingsWindow.xaml` |
| Desktop | `Documents/LeaveBalanceReportDocument.cs` (QuestPDF A4 report) |
| Tests | `tests/OptiPaie.Tests/LeaveServiceTests.cs` |

## 6. Tests

`LeaveServiceTests` — 26 integration tests against a **real SQLite file**:

- day counting (rest days excluded), rest-days-only period refused, invalid ranges
- overlap refused for live requests, allowed after a refusal, allowed for another employee
- approval writes the days into attendance; unpaid becomes `Absent` and shows up in the
  attendance summary payroll consumes; rest days skipped
- cancel and delete remove only the rows this module created — a manual pointage survives
- cancel then re-request the same period works
- lifecycle guards (edit after approval, approve after refusal)
- balance: full year = 30 days, pro-rated mid-year hire, approved-only counting,
  pending shown separately, unpaid tracked apart, cross-year split
- company-wide balances come from the shared employee table
- settings round-trip and immediately change both the count and the entitlement
- one stored representation per calendar day across modules

Status: **26/26 passing**, full suite **1219/1219 passing**, `OptiPaie.Desktop` builds
0 errors / 0 warnings.
