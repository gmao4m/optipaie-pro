# Pointage — Commit 1 baseline (measure, do not guess)

Measured 2026-11-16 against the owner's **real** database (`%AppData%\OptiPaie DZ\optipaie.db`) and the real `AttendanceMatrixView` driven through the `[Explicit]` perf harness. No code changed in this commit.

## TL;DR — what actually costs the time
1. **Cold `XamlReader.Parse` of the day-column templates on first open** — ~**0.5 s** one-time per session, re-paid on every month/year change. This is the dominant open cost (the code-behind comment already says so). **#1 target.**
2. **Building employee×day cell view-models** on every load — 20 ms @20 emp → 164 ms @500 emp. Scales with employees × days.
3. **KPI recompute** loops over *all* cells **twice**, on every load **and every single cell edit** — O(cells).
4. DataGrid realizing 600+ template cells — handled by virtualization, but at 20 employees everything is on-screen so virtualization buys nothing.
5. **The database is NOT the bottleneck** — 2 indexed queries, < 5 ms.

## Measurements

### Database
- `AttendanceRecords`: **499 rows** total. Busiest month **June-2026 = 440 rows** (≈20 employees × 22 weekdays); July 43, Sept 14, Aug 2.
- **Indexes (all present):** `UX_Attendance_Employee_Date` UNIQUE `(EmployeeId, WorkDate) WHERE IsDeleted=0`; `IX_Attendance_Date (WorkDate)`; `IX_Attendance_Employee (EmployeeId)`.
- **Query plan** for the month pull uses the index (`SEARCH … USING INDEX UX_Attendance_Employee_Date`; employees via `IX_Employees_CompanyId`). No scan.
- **Queries per screen open:** **2** — `Employees.GetByCompany` + `Attendance.GetCompanyMonth` (one `GetByCompanyRange`). A company switch adds **1** (`RebuildDepartments` → `GetByCompany`). **No N+1** — records are pulled once and keyed into a dictionary; the cell loop issues zero queries.
- **Single cell edit** (`PaintCell`): `cell.Paint` (1 cell) → `Attendance.SetDayStatus` (**1 upsert**) → `RecomputeKpis` (loops all cells twice). **It does NOT reload the grid** — good — but it recomputes all KPIs each time.
- Query time for ≤440 rows: sub-millisecond to low-ms. Negligible vs render.

### Render (perf harness, real view, full-month density)
| Employees | activate (data + cell VMs) | open (view + layout + render) | click→interactive |
|---|---:|---:|---:|
| 20 | 20 ms | **545 ms** (first open — cold `XamlReader.Parse`) | ≈565 ms |
| 150 | 40 ms | 30 ms (warm) | ≈70 ms |
| 500 | 164 ms | 55 ms (warm) | ≈219 ms |

The 545 ms at 20 employees is the **first** `XamlReader.Parse` in the session (parser cold-start + per-column parse). Once warm, opening 35 columns is 30–55 ms. So the owner's "slow to open" = the **~0.5 s cold first open**; month/year changes re-pay a smaller slice because the columns are rebuilt (the cache only skips dept/search/refresh).

### UI elements for one month of the real data (20 employees, 31-day month)
- **Rows:** 20. **Columns:** 35 (5 frozen identity + 30–31 day columns).
- **Cells:** 20 × 31 = **620** `MatrixCellViewModel` + 620 template cells.
- Plus **31 clickable day-header buttons**, **20 row select checkboxes**.
- Grid **does virtualize** (`EnableRowVirtualization`/`EnableColumnVirtualization`, Recycling, Pixel scroll) — but with 20 rows all visible, nothing is virtualized away.
- **Dominant realize cost = `XamlReader.Parse` per day column** in `AttendanceMatrixView.xaml.cs` `DayColumn()` (a XAML string parsed into a `DataTemplate`, once per column).

## Button / command inventory (the "cluttered" complaint)
| Control | What it does | Needed? |
|---|---|---|
| Department filter | filter rows by department | keep (useful) |
| Month / Year | pick the period | keep (essential) |
| Status filter combo | show only employees having status X | marginal — rarely used |
| Search box | find an employee | keep (useful at scale) |
| "Current month" button | jump to today's month | minor convenience |
| **Status palette (≈6 brushes + eraser)** | pick a "brush", then click cells to paint a status | **the confusing paint metaphor** — obsolete under present-by-default |
| "Apply month to selection" | bulk-fill the month for checked employees | obsolete under present-by-default |
| "More" menu (Select all, Clear selection, Month reports, Refresh, Settings) | 5 secondary actions | mostly removable; Settings/Reports move elsewhere |
| 31 day-header buttons | click a day to paint it for all rows | obsolete under present-by-default |
| 20 row checkboxes | select employees for bulk paint | obsolete under present-by-default |
| 5 KPI tiles | display only | keep (or trim) |

**The whole "paint-by-brush, fill-the-month, bulk-select" model is the clutter and part of the cost.** In an **exceptions-only** screen (Commit 4) a person is present unless a record says otherwise, so the palette, bulk-fill, day-header paint, and select checkboxes all disappear — you only record the exceptions.

## What payroll reads from attendance **today** (for Commit 5)
- `PayrollService.ResolveWorkedDays` itself reads **no attendance** — it prorates by **hire/exit dates only**, or uses `request.WorkedDays/WorkableDays` when the caller sets them.
- The caller sets them from attendance, in **two** places (identical logic): `PayrollViewModel` (lines 427–446) and `BatchPayrollService.BuildRequest` (258–276):
  - `summary = Attendance.GetMonthlySummary(emp, year, month)`
  - `WorkedDays = max(0, monthDays − summary.AbsentDays)` (full month when nothing recorded)
  - `WorkableDays = monthDays` ( **calendar days of the month, weekends included** )
- Consequences to fix later: the proration **denominator is calendar days, not work days** (Commit 2 adds the work calendar); only the single `Absent` status reduces pay (no justified/unjustified distinction, no half-day — Commits 3–5); "no attendance at all" silently means "full month present".

## Forward pointer (NOT changed here)
- Integer day counters that will widen to decimal in Commit 3 (full list produced there): `AttendanceSummary.PresentDays, AbsentDays, LeaveDays, HolidayDays, RestDays, RecordedDays, LateCount` and the derived `PaidDays`. Note `PayrollContext.WorkedDays/WorkableDays` and `request.WorkedDays/WorkableDays` are **already decimal**, so the engine path is half-day-ready; the integer gate is in `AttendanceSummary`.

## Ranked plan implied by the numbers
1. **Kill the per-column `XamlReader.Parse`** (use a single reusable/compiled cell template, or a fixed grid), removing the ~0.5 s cold open. *(Commit 4)*
2. Present-by-default, exceptions-only: fewer cell VMs, no palette/bulk/checkbox clutter. *(Commit 4)*
3. Incremental KPI/total update on edit instead of a full re-loop. *(Commit 4)*
4. Work calendar so proration uses real work days, not calendar days. *(Commit 2)*
5. Decimal day counters for half-days. *(Commit 3)*
6. A visible pre-run review table wiring attendance → payroll. *(Commit 5)*

Indexes are already adequate; no missing index was found for the month pull (Commit 4 will re-check once the read shape changes).
