# Module 5 — Évaluations (Performance)

Premium module, module key `performance`. Fifth module of the HR ecosystem. It runs
weighted performance reviews on a /20 scale and **pulls the employee's attendance context
live from the Attendance module** — absences, retards and heures supplémentaires appear
in every review without being copied or re-entered.

---

## 1. What it does

| Capability | Where |
|---|---|
| Reviews of a company for a year | `Évaluations` screen |
| Create / edit a review with weighted criteria | `Nouvelle évaluation` dialog |
| Live overall score (/20) and rating band | editor |
| Attendance context (absences, retards, h. supp.) | editor + PDF |
| Finalise · Reopen · Delete | action bar |
| Review PDF (A4, FR) | `PDF` action |

## 2. Business rules (single source of truth)

All rules live in `OptiPaie.Services/PerformanceService.cs`.

- **Weighted /20 scoring**: overall = Σ(score × weight) / Σ(weight), rounded to 2, on a
  0–20 scale. Scores must be within 0–20; weights non-negative.
- **Rating bands**: ≥16 Excellent · ≥14 Très bien · ≥12 Bien · ≥10 Assez bien · <10
  Insuffisant.
- **Default criteria** seeded on a new review: qualité du travail, productivité,
  assiduité/ponctualité, travail d'équipe, initiative — each weight 1.
- **Lifecycle**: `Draft → Completed`, reopenable. A review may only be edited while a
  draft; finalising requires a reviewer and at least one weighted criterion.
- **Criteria are child rows of a draft** — a save replaces them wholesale.

## 3. Cross-module synchronisation (live attendance pull)

`GetDetail` aggregates the employee's attendance across the review year straight from the
Attendance module (`IAttendanceService.GetMonthlySummary` over the 12 months) and returns
an `AttendanceContext` (absences, retards, heures travaillées/supplémentaires). It is
**read every time**, never stored on the review, so a review always reflects the latest
pointage — even attendance recorded *after* the review was created. When there is no
pointage (or the Attendance module is not in use) the section is simply omitted.

This is a pure read: the Attendance module, the payroll engine, the licensing system and
the module-activation system are all untouched.

## 4. Data model

Migration `src/OptiPaie.Data/Sql/Migrations/0014_Performance.sql` — additive only.

```
PerformanceReviews
  Id INTEGER PK   EmployeeId → FK Employees(Id)   -- the SHARED employee table
  PeriodYear / PeriodLabel   Status (1 Draft, 2 Completed)
  ReviewDate   Reviewer   OverallScore TEXT (/20, derived)   Comments
  CreatedAtUtc / UpdatedAtUtc / IsDeleted

PerformanceCriteria
  Id INTEGER PK   ReviewId → FK PerformanceReviews(Id) ON DELETE CASCADE
  Label   Weight TEXT   Score TEXT (/20)   Comment   SortOrder   IsDeleted
```

No company column: a company's reviews come from joining `Employees`. Dates bind through
`SqliteDate.Day`.

## 5. Files

| Layer | File |
|---|---|
| Core | `Enums/PerformanceStatus.cs`, `Entities/PerformanceReview.cs`, `Entities/PerformanceCriterion.cs`, `Dtos/PerformanceSummary.cs` |
| Core | `Interfaces/Repositories/IPerformanceRepository.cs`, `Interfaces/Services/IPerformanceService.cs` |
| Data | `Sql/Migrations/0014_Performance.sql`, `Repositories/PerformanceRepository.cs` |
| Services | `PerformanceService.cs` (takes `IAttendanceService` for the live pull) |
| Desktop | `ViewModels/PerformanceViewModel.cs`, `PerformanceEditViewModel.cs` |
| Desktop | `Views/PerformanceView.xaml`, `PerformanceEditWindow.xaml` |
| Desktop | `Documents/PerformanceReviewDocument.cs` (QuestPDF A4) |
| Tests | `tests/OptiPaie.Tests/PerformanceServiceTests.cs` |

## 6. Tests

`PerformanceServiceTests` — 17 integration tests against a **real SQLite file**:

- draft creation seeds the five default criteria; unknown employee rejected
- weighted /20 scoring (worked example 14/20), score-out-of-range rejected, criteria
  replaced wholesale
- rating bands (parameterised)
- lifecycle: complete needs a reviewer, locks against editing; reopen re-enables; delete
- **cross-module pull**: a review's detail reflects attendance recorded after creation
  (1 absence, 1 retard, 2 h supp.); with no pointage the context is omitted
- company listing carries the shared name and the rating

Status: **17/17 passing**, full suite **1276/1276 passing**, `OptiPaie.Desktop` builds
0 errors / 0 warnings.
