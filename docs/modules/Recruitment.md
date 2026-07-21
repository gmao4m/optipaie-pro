# Module 8 — Recrutement / ATS

Premium module, module key `ats`. Eighth module of the HR ecosystem. It manages job
postings and a candidate pipeline. Its defining feature is the **deepest ecosystem link**:
hiring a candidate **creates the shared employee record**, so the new hire flows straight
into Contracts, Payroll and every other module with no re-entry.

---

## 1. What it does

| Capability | Where |
|---|---|
| Job postings of a company | `Recrutement` screen |
| Create / edit a posting | `Nouvelle offre` dialog |
| Close · Reopen · Delete | action bar |
| Candidate pipeline (add, move stage, hire, reject) | `Candidats` dialog |
| Open-postings + candidate KPIs | KPI strip |

## 2. Business rules (single source of truth)

All rules live in `OptiPaie.Services/AtsService.cs`.

- **Pipeline stages**: Applied → Screening → Interview → Offer → **Hired** / Rejected.
  `MoveStage` handles the intermediate stages; Hired and Rejected have their own methods.
- **Hiring** is the only way to reach the `Hired` stage; it is idempotent-guarded so a
  candidate can never be hired twice (no duplicate employee).
- **Posting auto-fill**: a posting flips to `Filled` once its number of positions has been
  hired.
- **Candidate deletion guard**: a hired candidate cannot be deleted from the ATS — the
  real employee lives in the shared table and is managed from the Employés module.
- Validation: posting title + company required, positions ≥ 1; candidate last name required.

## 3. Cross-module synchronisation (hire creates the shared employee)

`Hire(candidateId)` runs in **one transaction**:

1. inserts a new `Employee` in the shared table for the posting's company, seeded from the
   candidate (last/first name, `Poste` = the posting title) with sensible defaults for the
   fields recruitment doesn't capture (HR completes the record and issues a contract);
2. sets the candidate to `Hired` and stores the new `HiredEmployeeId`;
3. fills the posting when its positions are met.

From that moment the new hire is a first-class shared employee — visible to Contracts
(issue the contract, which then syncs salary/type), Payroll, Attendance, Leave, Loans,
Assets and Training. The payroll engine, licensing and module-activation systems are
untouched.

## 4. Data model

Migration `src/OptiPaie.Data/Sql/Migrations/0017_Ats.sql` — additive only.

```
JobPostings
  Id INTEGER PK   CompanyId → FK Companies(Id)
  Title   Department   Description   Status (1 Open, 2 Closed, 3 Filled)
  OpenDate   Positions (>=1)   Notes   CreatedAtUtc / UpdatedAtUtc / IsDeleted

Candidates
  Id INTEGER PK   PostingId → FK JobPostings(Id) ON DELETE CASCADE
  FirstName / LastName   Phone / Email   Stage (1..6)   Rating (0..5)
  Source   Notes   AppliedDate   HiredEmployeeId (set on hire, → shared Employees)
  CreatedAtUtc / UpdatedAtUtc / IsDeleted
```

Candidates are **not** employees — they exist only in this module until hired. Dates bind
through `SqliteDate.Day`.

## 5. Files

| Layer | File |
|---|---|
| Core | `Enums/JobStatus.cs` (+ `CandidateStage`), `Entities/JobPosting.cs`, `Entities/Candidate.cs`, `Dtos/JobPostingSummary.cs` |
| Core | `Interfaces/Repositories/IAtsRepository.cs`, `Interfaces/Services/IAtsService.cs` |
| Data | `Sql/Migrations/0017_Ats.sql`, `Repositories/AtsRepository.cs` |
| Services | `AtsService.cs` |
| Desktop | `ViewModels/AtsViewModel.cs`, `AtsDialogViewModels.cs` (posting/pipeline/candidate) |
| Desktop | `Views/AtsView.xaml`, `AtsPostingEditWindow.xaml`, `AtsPipelineWindow.xaml`, `AtsCandidateEditWindow.xaml` |
| Tests | `tests/OptiPaie.Tests/AtsServiceTests.cs` |

## 6. Tests

`AtsServiceTests` — 12 integration tests against a **real SQLite file**:

- posting creation defaults to Open; title required
- candidate starts Applied; `MoveStage` advances; moving straight to Hired is refused
- **hire creates the shared employee** (right company, name and `Poste`), links it to the
  candidate, and marks the candidate Hired
- hire fills the posting when positions are met; does not fill while positions remain
  (two-position posting needs two hires → two shared employees)
- double-hire refused (no duplicate employee); deleting a hired candidate refused
- company listing returns candidate/hired counts; reject moves the candidate out

Status: **12/12 passing**, full suite **1315/1315 passing**, `OptiPaie.Desktop` builds
0 errors / 0 warnings.
