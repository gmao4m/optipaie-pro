# Module 7 — Formation (Training)

Premium module, module key `training`. Seventh module of the HR ecosystem. It organises
training sessions and enrols employees in them. Sessions belong to a **Company**; each
enrolment references a **shared employee**, so participants and training history always
point at the live employee record — never a copy.

---

## 1. What it does

| Capability | Where |
|---|---|
| Training sessions of a company | `Formation` screen |
| Create / edit a session | `Nouvelle formation` dialog |
| Start · Complete · Cancel (status) | action bar |
| Enrol participants, record result/score/certificate | `Participants` dialog |
| Planned/ongoing count + total budget KPIs | KPI strip |
| Per-employee training history (service) | `GetEmployeeHistory` |

## 2. Business rules (single source of truth)

All rules live in `OptiPaie.Services/TrainingService.cs`.

- **One enrolment per employee per session** — enforced by the service and by a partial
  unique index (`WHERE IsDeleted = 0`).
- **Lifecycle**: `Planned → Ongoing → Completed`, or `Cancelled`. Enrolling into a
  cancelled session is refused.
- **Outcomes**: each participant carries a `TrainingResult` (Inscrit / Réussi / Échoué /
  Absent) plus an optional score and certificate reference. `CompletedCount` on the
  summary counts the `Réussi` participants.
- Validation: title and start date required, end date not before start, cost non-negative,
  the enrolled employee must be a real shared employee.

## 3. Cross-module data sharing

Participants reference the shared `Employees` table by foreign key; every name shown
(`TrainingParticipantSummary.EmployeeName`, the history) is resolved from that record at
read time. `GetEmployeeHistory` gathers every session an employee attended — an
employee-centric view built purely from the shared link. No employee or company data is
duplicated; the payroll engine, licensing and module-activation systems are untouched.

## 4. Data model

Migration `src/OptiPaie.Data/Sql/Migrations/0016_Training.sql` — additive only.

```
TrainingSessions
  Id INTEGER PK   CompanyId → FK Companies(Id)
  Title   Category   Provider   Status (1 Planned, 2 Ongoing, 3 Completed, 4 Cancelled)
  StartDate   EndDate   Location   Cost TEXT   Notes
  CreatedAtUtc / UpdatedAtUtc / IsDeleted

TrainingParticipants
  Id INTEGER PK   SessionId → FK TrainingSessions(Id) ON DELETE CASCADE
  EmployeeId → FK Employees(Id)                 -- the SHARED employee table
  Result (1 Enrolled, 2 Completed, 3 Failed, 4 Absent)   Score   CertificateRef   Notes
  CreatedAtUtc / IsDeleted
  UNIQUE (SessionId, EmployeeId) WHERE IsDeleted = 0     -- one enrolment per employee
```

Dates bind through `SqliteDate.Day`.

## 5. Files

| Layer | File |
|---|---|
| Core | `Enums/TrainingStatus.cs` (+ `TrainingResult`), `Entities/TrainingSession.cs`, `Entities/TrainingParticipant.cs`, `Dtos/TrainingSummary.cs` |
| Core | `Interfaces/Repositories/ITrainingRepository.cs`, `Interfaces/Services/ITrainingService.cs` |
| Data | `Sql/Migrations/0016_Training.sql`, `Repositories/TrainingRepository.cs` |
| Services | `TrainingService.cs` |
| Desktop | `ViewModels/TrainingViewModel.cs`, `TrainingEditViewModel.cs` (+ participants VMs) |
| Desktop | `Views/TrainingView.xaml`, `TrainingEditWindow.xaml`, `TrainingParticipantsWindow.xaml` |
| Tests | `tests/OptiPaie.Tests/TrainingServiceTests.cs` |

## 6. Tests

`TrainingServiceTests` — 13 integration tests against a **real SQLite file**:

- creation defaults to Planned; title required; end-before-start rejected; status
  lifecycle
- enrol adds the shared employee (name resolved); double-enrol rejected; unknown employee
  rejected; enrol into a cancelled session rejected
- outcome recording (result/score/certificate) and its effect on `CompletedCount`
- removing a participant frees re-enrolment
- `GetEmployeeHistory` lists an employee's sessions only; `GetByCompany` returns counts
- delete removes the session

Status: **13/13 passing**, full suite **1303/1303 passing**, `OptiPaie.Desktop` builds
0 errors / 0 warnings.
