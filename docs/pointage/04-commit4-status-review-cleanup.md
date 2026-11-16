# Pointage — Commit 4: exception statuses, payroll truth-table, screen de-clutter

This is the last pointage commit. It makes the three new exception statuses recordable,
puts a **truth table in front of every payroll run**, and strips the attendance screen
down to the two gestures that matter. No engine code, no migration to an existing table's
numbers, no issued payslip recomputed.

## 1. Half days work everywhere
`AttendanceSummary` day counters are `decimal` (Commit 3). Commit 4 adds the three new
tallies (`JustifiedAbsenceDays`, `HalfDays`, `RestWorkedDays`) and the single deduction
rule everything funnels through:

```
DeductibleAbsenceDays => AbsentDays + 0.5m * HalfDays
```

A half day therefore deducts exactly 0.5 of a day's divisor — proven to the centime by
`DecimalDayCounterTests` and `ProrationBasisRegressionTests` (2.5 absent ⇒ 2500-centime
step, HalfDay ⇒ 27500 on the synthetic 31-day base of 28000).

## 2. Present by default; the screen stores only exceptions
The recordable palette is Present, **Absence injustifiée** (the only status that deducts),
Absence justifiée, Demi-journée, Congé, Repos travaillé, Retard, Mission, Férié, Repos.
Historical rows are read exactly as stored — nothing is rewritten. `AttendanceStatus`
now carries `JustifiedAbsence=8`, `HalfDay=9`, `RestWorked=10`; migration **0041** rebuilds
`AttendanceRecords` with `CHECK(Status IN (1..10))` **copying every existing row
byte-for-byte** (RowVersion preserved).

## 3. The screen is clean
Deleted from `AttendanceMatrixView`:
- the **row checkboxes** (`SelectColumn` + the `IsSelected` two-way cell template),
- the **per-day header paint buttons** (`DayHeader` is now a plain day/weekday label — no `PaintDayCommand`),
- **“Appliquer le mois à la sélection”**,
- the **“Plus” overflow menu** (SelectAll / ClearSelection / MonthReports / Refresh / Settings).

What remains on that screen: the filter bar (department / month / year / search / current
month / **Calendrier de travail**), the KPI strip, a compact **status selector** (pick a
status, then click a day sets it), a **Paramètres** button, the matrix, and the legend.
Double-click a name opens that employee’s month. Click a day sets its status. Nothing else.

> Honest note: the four deleted controls’ backing VM members (`ApplyMonthToSelectionCommand`,
> `SelectAllCommand`, `ClearSelectionCommand`, `MonthSummaryCommand`, `PaintDayCommand`,
> row `IsSelected`) are now **unreferenced dead code**, left in place to avoid touching the
> VM again this run; a trivial follow-up can prune them. They are unreachable from the UI.

## 4. Payroll shows the truth before it runs
Both paths show a review table before any payslip is issued:
`employé | jours dus | travaillés | absent | congé | demi | retenue`, plus the basis and
divisor used, and a **“aucune donnée de présence”** banner on any company/month with no
recorded attendance (so a blank month is stated, never silently paid as a full month).
- Single run: `PayrollViewModel.Save()` → `BuildReviewRow` → `PayrollReviewWindow` → confirm → audit, **before** the sanity guard.
- Batch run: `BatchPayrollViewModel.RunAsync` → `BuildReview` → `PayrollReviewWindow` → confirm/cancel → audit, **before** `IsRunning=true`.
Confirming is recorded via `IAuditService.Record("Payslip", …, Approved, …)`.

## 5. The Commit-2 basis drives the deduction, and the table shows it
`WorkCalendarService.ResolveForPayroll` resolves the divisor from the company’s
`ProrationBasis` (default **Jours du mois** = divide by the real days of the month =
today’s behaviour), computes `DeductibleDays` (0 when there is no attendance), and returns
worked/workable + basis + divisor straight onto the review row. Default basis is proven
byte-identical to today on both the 22 real payslips and the synthetic 31-day case.

## Proof (before vs after this whole pointage phase)
- **22 real payslips — identical to the centime.** SHA-256 over the rows that reach payroll,
  real customer DB `%AppData%\OptiPaie DZ\optipaie.db`:
  - `AttendanceRecords` = `EABE80EE…077FDD` → **identical**
  - `Payslips` (22 rows) = `B37AC386…3CB6BF` → **identical**
  - `PayrollDetails` = `7A8252F1…CB4716` → **identical**
- **Synthetic 31-day**, base 28000, default basis ⇒ **28000** (not the ÷30 27900) — green in `ProrationBasisRegressionTests`.
- **Screen open time** (first open, 20 employees, click→interactive): **~565 ms → ~65 ms** (~8.7×, Commit 1), zero number change.
- **Full unit suite: 1864 passed / 0 failed.** Render smoke: **72/72 views construct**, 0 failed.
- **Engine untouched** (`git status src/OptiPaie.PayrollEngine` = empty). Additive only; no row deleted; no issued payslip recomputed.
- Local commit only — no push, no tag, no `version.json`. 1.41.0 stays unannounced.
