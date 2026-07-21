# Module 4 — Contrats (Contracts)

Premium module, module key `contracts`. Fourth module of the HR ecosystem. It manages
employment contracts and is the module that **keeps the shared employee record current**:
activating a contract writes its salary, type and position onto the employee — so payroll
uses the terms in force — and terminating one sets the employee's exit date. No employee
or company data is duplicated.

---

## 1. What it does

| Capability | Where |
|---|---|
| Contracts of a company with expiry positions | `Contrats` screen |
| Create / edit a draft contract | `Nouveau contrat` dialog |
| Activate · Terminate · Renew · Delete | action bar |
| Expiry alerts (fixed-term ending within 30 days) | KPI strip |
| Generate the contract PDF (A4, FR) | `PDF` action |
| Active-contract + expiring KPIs | KPI strip |

## 2. Business rules (single source of truth)

All rules live in `OptiPaie.Services/ContractService.cs`.

- **One active contract per employee.** Activating a contract supersedes the current
  active one (marked `Renewed` if it is this contract's predecessor, otherwise `Expired`).
- **Draft → Active → (Expired | Terminated | Renewed).** Only a `Draft` can be activated;
  only an `Active` contract can be terminated; `Active`/`Expired` can be renewed.
- **A CDI has no end date**; a fixed-term contract (CDD, apprentissage, pré-emploi)
  requires one, and the end date must follow the start.
- **Edit guard**: a contract in force keeps its legal terms — `Save` on a non-draft
  contract updates only the reference, signature date and notes. Terms change through
  `Renew` or `Terminate`.
- **Delete guard**: an active contract cannot be deleted — terminate it first.
- Trial period 0–365 days; base salary positive.

## 3. Cross-module synchronisation (the shared employee)

Every lifecycle action that changes the terms in force writes back to the shared
`Employees` table **inside one transaction**:

| Action | Effect on the shared employee |
|---|---|
| **Activate** | `BaseSalary`, `ContractType`, `Poste` ← contract; `ExitDate` cleared; `IsActive = true` |
| **Terminate** | `ExitDate` ← effective date; `IsActive = false` |
| **Renew** | new active contract chained to the old; employee synced to the new terms |

Because payroll reads `Employee.BaseSalary` / `ContractType`, a salary agreed in a
contract reaches the next payslip with **no** change to the payroll engine. The engine,
the licensing system and the module-activation system are all untouched.

## 4. Data model

Migration `src/OptiPaie.Data/Sql/Migrations/0013_Contracts.sql` — additive only.

```
EmploymentContracts
  Id INTEGER PK   EmployeeId → FK Employees(Id)   -- the SHARED employee table
  Type (reuses ContractType: 1 CDI, 2 CDD, 3 Apprenticeship, 4 Internship, 99 Other)
  Status (1 Draft, 2 Active, 3 Expired, 4 Terminated, 5 Renewed)
  Reference / Position   BaseSalary TEXT (invariant decimal)
  StartDate   EndDate (null for CDI)   CHECK EndDate >= StartDate
  TrialPeriodDays   PreviousContractId (renewal chain)   SignedDate   Notes
  CreatedAtUtc / UpdatedAtUtc / IsDeleted
```

No company column: a company's contracts come from joining `Employees`. Dates bind
through `SqliteDate.Day` (see [Leave](Leave.md) §4) for one canonical representation.

## 5. Files

| Layer | File |
|---|---|
| Core | `Enums/ContractStatus.cs`, `Entities/EmploymentContract.cs`, `Dtos/ContractSummary.cs` |
| Core | `Interfaces/Repositories/IContractRepository.cs`, `Interfaces/Services/IContractService.cs` |
| Data | `Sql/Migrations/0013_Contracts.sql`, `Repositories/ContractRepository.cs` |
| Services | `ContractService.cs` |
| Desktop | `ViewModels/ContractViewModel.cs`, `ContractEditViewModel.cs` (+ terminate/renew dialog VMs) |
| Desktop | `Views/ContractView.xaml`, `ContractEditWindow.xaml`, `ContractTerminateWindow.xaml`, `ContractRenewWindow.xaml` |
| Desktop | `Documents/ContractDocument.cs` (QuestPDF A4 contract) |
| Tests | `tests/OptiPaie.Tests/ContractServiceTests.cs` |

## 6. Tests

`ContractServiceTests` — 18 integration tests against a **real SQLite file**:

- validation (CDD needs an end date, CDI clears it, unknown employee rejected, draft
  changes nothing on the employee)
- activation writes the terms onto the shared employee, supersedes the previous active
  contract, is a no-op when already active
- termination sets the exit date and deactivates the employee; a non-active contract
  cannot be terminated
- renewal chains a new active contract (predecessor `Renewed`, no trial period, employee
  synced); a draft cannot be renewed
- expiry alerts: fixed-term within the window (incl. overdue), supersession leaves one
  active contract, CDIs ignored
- an active contract's `Save` only touches reference/notes and never the employee salary
- delete guard on active, delete allowed on draft, company listing carries the shared name

Status: **18/18 passing**, full suite **1259/1259 passing**, `OptiPaie.Desktop` builds
0 errors / 0 warnings.
