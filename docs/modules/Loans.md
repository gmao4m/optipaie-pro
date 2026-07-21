# Module 3 — Prêts et avances (Loans)

Premium module, module key `loans`. Third module of the HR ecosystem. It grants loans
and salary advances and **recovers the monthly instalment on the payslip** — the
instalment is fed into payroll as a deduction (input only) and recorded against the loan
exactly once when the payroll is saved. The payroll engine is never modified.

---

## 1. What it does

| Capability | Where |
|---|---|
| Loans of a company with their outstanding balances | `Prêts et avances` screen |
| Create / edit a loan with live instalment count | `Nouveau prêt` dialog |
| Suspend · Resume · Cancel · Delete | action bar |
| Repayment ledger + manual repayment | `Détail / Remboursements` dialog |
| Total encours + active-loan KPIs | KPI strip |
| Automatic payslip deduction + recovery on save | payroll worksheet |

## 2. Business rules (single source of truth)

All rules live in `OptiPaie.Services/LoanService.cs`.

- **The outstanding balance is never stored.** It is always `Principal − Σ repayments`,
  so it cannot drift. `RemainingInstallments = ceil(outstanding / instalment)`.
- **One recovery per (loan, period)** — enforced by the service and by a partial unique
  index (`WHERE IsDeleted = 0`). A period is never deducted twice.
- **The instalment is capped at the balance**: the last month recovers only what is
  left, never more.
- **Reproducible deduction**: `GetMonthlyDeduction` returns a period's *recorded* amount
  if it exists, otherwise the scheduled instalment — so re-opening a saved payslip shows
  the same figure.
- **Auto-settlement**: a loan flips to `Settled` the moment its balance reaches zero and
  is no longer deducted. Removing the final repayment reopens it to `Active`.
- **Status**: `Suspended` loans are skipped by the deduction; `Cancelled` loans are never
  deducted and never counted in the outstanding total.
- **Edit guard**: the principal can never be set below what has already been repaid.

Validation: employee must exist, principal and instalment positive, instalment ≤
principal, start month 1–12.

## 3. Payroll integration (engine untouched)

Entirely in the desktop worksheet + the Loans service — no engine change:

1. **On load** (`PayrollViewModel.LoadWorksheet`): if the module is licensed and
   `GetMonthlyDeduction(employee, year, month) > 0`, a **"Remboursement prêt"** line is
   added as an ordinary manual deduction. The accountant can remove it to skip the
   recovery for the month.
2. **On save** (`PayrollViewModel.Save`, after `Payroll.Generate` archives the payslip):
   if that line is still present, `RecordPayrollDeductions` writes the recovery against
   every active loan, once. It is idempotent per period, so re-saving is safe.

Because a preview never records, the loan balance only moves when a payslip is actually
archived. Editing the line's amount by hand changes the deducted figure on that payslip
but the recorded recovery always follows the loan schedule, keeping the ledger exact.

## 4. Data model

Migration `src/OptiPaie.Data/Sql/Migrations/0012_Loans.sql` — additive only.

```
Loans
  Id INTEGER PK   EmployeeId → FK Employees(Id)   -- the SHARED employee table
  Type (1 Loan, 2 Advance)   Status (1 Active, 2 Settled, 3 Suspended, 4 Cancelled)
  Principal / MonthlyInstallment  TEXT (invariant decimal)
  StartYear / StartMonth   Reason / Notes
  CreatedAtUtc / UpdatedAtUtc / IsDeleted

LoanRepayments
  Id INTEGER PK   LoanId → FK Loans(Id) ON DELETE CASCADE
  Year / Month   Amount TEXT   IsManual   CreatedAtUtc / IsDeleted
  UNIQUE (LoanId, Year, Month) WHERE IsDeleted = 0     -- anti double-deduction
```

No company column: a company's loans come from joining `Employees`.

## 5. Files

| Layer | File |
|---|---|
| Core | `Enums/LoanType.cs`, `Enums/LoanStatus.cs`, `Entities/Loan.cs`, `Entities/LoanRepayment.cs`, `Dtos/LoanSummary.cs` |
| Core | `Interfaces/Repositories/ILoanRepository.cs`, `Interfaces/Services/ILoanService.cs` |
| Data | `Sql/Migrations/0012_Loans.sql`, `Repositories/LoanRepository.cs` |
| Services | `LoanService.cs` |
| Desktop | `ViewModels/LoanViewModel.cs`, `LoanEditViewModel.cs`, `LoanDetailViewModel.cs` |
| Desktop | `Views/LoanView.xaml`, `LoanEditWindow.xaml`, `LoanDetailWindow.xaml` |
| Desktop | `ViewModels/PayrollViewModel.cs` (deduction line + recovery on save), `PayrollLineVM.cs` (`IsLoan`) |
| Tests | `tests/OptiPaie.Tests/LoanServiceTests.cs` |

## 6. Tests

`LoanServiceTests` — 22 integration tests against a **real SQLite file**:

- validation (instalment ≤ principal, positive amounts, real employee)
- schedule: nothing before the start period, several loans summed, last instalment
  capped at the balance, suspended loan not deducted
- payroll recording reduces the balance, is idempotent per period, settles at zero,
  never starts before the loan begins
- deduction reproducible after recording
- manual repayments reduce the balance, reject over-payment and a duplicate period,
  removing the last repayment reopens a settled loan, manual + payroll never double-count
- company listing carries the shared employee name, outstanding excludes cancelled loans
- delete removes the loan and its repayments; principal cannot fall below repaid

Status: **22/22 passing**, full suite **1241/1241 passing**, `OptiPaie.Desktop` builds
0 errors / 0 warnings.
