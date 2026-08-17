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

- **Day counting** excludes the company weekly rest days — **Friday and Saturday by
  default, configurable** in the Paramètres dialog — unless exclusion is turned off. A
  period made only of rest days is refused. There is **no public-holidays calendar** in
  the application, so holidays are *not* auto-excluded (only the weekly rest is); a day can
  still be marked by hand in the Attendance screen.
- **No overlap**: two *live* requests (En attente or Approuvée) of one employee may never
  cover the same day. Refused, cancelled and **Brouillon (draft)** requests do not block —
  a draft is not "live". The rule is re-checked when a draft is submitted.
- **Annual entitlement** = 2,5 days per month worked in the year, capped at 30
  (loi 90-11 art. 41). Pro-rated from the hire date and, when applicable, the exit
  date — both read from the shared employee record.
- **Balance** carries four figures: **acquis** (entitlement), **consommé** (approved annual
  days), **réservé** (submitted-but-undecided annual days) and **disponible**
  (acquis − consommé − réservé). A *submitted* request reserves the balance; a **draft
  reserves nothing**. A refusal or a cancellation releases the reservation. Only Congé annuel
  consumes the balance; sick / maternity / special never touch it; unpaid days are tracked on
  their own line. Each type carries two policy flags — `DecrementsAnnualBalance` and `IsPaid`
  — centralised in `Enums/LeaveTypePolicy`. **The balance is always derived, never a stored
  counter**, so no value is ever decremented before its write commits.
- **Employee eligibility**: the employee must be employed on the leave's start date (hired
  on or before it, not exited before it).
- **State machine** — enforced in the SERVICE, not only the UI; every forbidden transition
  is rejected with a `Result` error: **Brouillon → En attente → (Approuvée | Refusée) →
  Annulée**. `Submit` turns a draft live; `ReturnToDraft` sends an En attente back. Only an
  En attente can be approved/refused; only an Approuvée can be cancelled. A **motive is
  mandatory** for a refusal and a cancellation. Brouillon is stored additively as `IsDraft=1`
  (migration 0030), so the `Status` CHECK (1..4) is never touched.
- **Approval is atomic**: the status change and the attendance rows are written in one
  transaction — a mid-approval failure rolls both back and records no audit, leaving no
  partial row.
- **Cross-year requests** count, for a given year, only the days that fall inside it, and
  the balance is checked per year.

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

### How the payroll engine consumes leave *today* (unchanged)

The payroll engine has **no knowledge of leave**. The only path is the attendance summary:
`BatchPayrollService` computes `workedDays = monthDays − AttendanceSummary.AbsentDays`, and
`PayrollService`/`PayrollCalculationEngine` receive that `WorkedDays` figure. An approved
**unpaid** leave day is written as `Absent`, so it raises `AbsentDays` and lowers
`WorkedDays` → the salary is reduced exactly as a hand-entered absence would reduce it. A
**paid** leave day is written as `Congé` (`AttendanceStatus.Leave`), which is *not* an
absence, so `WorkedDays` — and therefore the amount — is unchanged. Proven by
`LeaveWorkflowTests.Payroll_PaidLeaveKeepsTheAmount_UnpaidDeductsOnlyViaAttendance`. **No
file under `OptiPaie.PayrollEngine` was modified.**

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
| Core | `Enums/LeaveType.cs`, `Enums/LeaveStatus.cs`, `Enums/LeaveTypePolicy.cs`, `Entities/LeaveRequest.cs` (`IsDraft`), `Dtos/LeaveBalance.cs` (`Available`, `WeekendDays`) |
| Core | `Interfaces/Repositories/ILeaveRepository.cs`, `Interfaces/Services/ILeaveService.cs` (`Submit`, `ReturnToDraft`) |
| Data | `Sql/Migrations/0010_Leave.sql`, `0011_NormaliseAttendanceDates.sql`, `0030_LeaveDraft.sql`, `Repositories/LeaveRepository.cs`, `Context/SqliteDate.cs` |
| Services | `LeaveService.cs` |
| Desktop | `ViewModels/LeaveViewModel.cs`, `LeaveEditViewModel.cs`, `LeaveBalancesViewModel.cs`, `LeaveSettingsViewModel.cs` |
| Desktop | `Views/LeaveView.xaml`, `LeaveEditWindow.xaml`, `LeaveBalancesWindow.xaml`, `LeaveSettingsWindow.xaml` |
| Desktop | `Documents/LeaveBalanceReportDocument.cs` (QuestPDF A4 report) |
| Desktop | `Views/TextPromptWindow.xaml` (themed motive prompt), `Common/Dialogs.Prompt` |
| Tests | `tests/OptiPaie.Tests/LeaveServiceTests.cs`, `LeaveWorkflowTests.cs` (mandatory proofs) |

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

`LeaveWorkflowTests` — the mandatory Congés proofs (11 tests): the full **state machine**
(every allowed transition passes, every forbidden one is rejected), the **reserved/available
balance** (reservation, release on refusal, release on cancellation, a draft reserving
nothing, insufficient-balance refusal), **overlap** (including a draft that is not live),
**atomicity** (a mid-approval attendance failure — injected through a failing UnitOfWork —
leaves no partial status/attendance/audit row), **payroll non-regression** (paid leave keeps
the amount; unpaid deducts only via the attendance summary, the engine untouched), and a full
create → submit → approve **smoke**.

Status: **Leave 26 + 11 proofs passing**, full suite **1526/1526 passing** (1 skipped
manual doc-inspection test), client + tests build 0 errors.
