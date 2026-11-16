# Pointage — Commit 2: the work calendar + proration basis

Additive, per company. Every default reproduces today's behaviour, so an untouched company is unchanged. The payroll engine is **not** touched; this commit adds the data + the service + the setting screen. Wiring the basis into the payroll run (with the review table) is Commit 4.

## What was added
- **Migration 0040** — `CompanyWorkCalendar` (one row per company): weekly pattern (`WorkMon…WorkSun`), `HalfDayHours`, `RestDayRate`, `ProrationBasis`. No per-company row is seeded — a missing row means "defaults", so nothing a company already set is overwritten. Holidays **reuse the existing `Holidays` calendar** (migration 0032) — not duplicated.
- **`WorkCalendarService`** — `GetSettings`/`SaveSettings` (save requires `ManageAttendance`), `GetWorkableDays(emp, year, month)` (work-pattern days inside the employment window, minus holidays), and the proration maths (`Denominator`, `Deduction`, `BuildExample`).
- **Settings screen** (`WorkCalendarWindow`, reached from the attendance "⋯ More" menu): the weekly work days, the three-basis radio with honest bilingual labels, a **live worked example** (the company's first employee, 3 absences, the current month, the deduction under each basis side by side), the rest-day rate and half-day hours, and an on-screen note that **changing the basis affects FUTURE runs only**.

## The proration basis (your Option 1 — confirmed)
Three bases, default = today:

| Basis | Divisor | Status |
|---|---|---|
| **Jours du mois / أيام الشهر** | ÷ the month's real days (28–31) | **DEFAULT — reproduces today exactly; zero movement** |
| Trentièmes fixes / أثلاث ثابتة | ÷ 30 always | opt-in (some collective agreements) |
| Jours ouvrables / أيام العمل | ÷ the workable days from the calendar | opt-in (the precise basis) |

Defaults chosen so nobody moves: `ProrationBasis = MonthDays`, `RestDayRate = 1` (no premium, as today), weekly pattern = Algerian Sun–Thu work / Fri–Sat rest (matches the attendance screen's current hard-coded weekend).

## Preserved exactly (your instructions)
- **Only `Absent` reduces pay today — unchanged.** I did not change which statuses deduct. (If another status should deduct, that is a separate decision — not implemented.)
- **"No attendance data = full month present" — unchanged.** (It will be stated explicitly on the Commit 4 review table so nobody is surprised.)
- The basis is **not yet consumed by payroll** — Commit 4 wires it. So this commit changes no payslip.

## Tests (RULE 0, hand-computed) — all green
- Default basis divisor = the month's real days (28/29/30/31) and **reproduces today's deduction** (base × absent ÷ real days): ÷28 = 3214.29, ÷29 = 3103.45, ÷30 = 3000.00, ÷31 = 2903.23.
- Three bases give three distinct results for the same employee/month.
- `GetWorkableDays`: full June = 22, mid-month hire (10 Jun) = 15, departure (15 Jun) = 11, July with the 5 Jul civil holiday = 21, plus a manual holiday = 20.
- Settings round-trip; defaults = today; `BuildExample` shows all three with the default marked.
- **Synthetic 31-day regression** (because the real data is all June): a real payroll run with 3 absences in July 2026 still prorates **÷31** (SalaireBrut 28000, not the ÷30 value 27900) — pins the default and guards Commit 4.

## Byte-identical proof
- Real DB **Payslips** `B37AC386…B6BF` and **AttendanceRecords** `EABE80EE…7FDD` — **identical** to before this phase. The `CompanyWorkCalendar` table does not exist in the real DB yet (migration 0040 runs additively on the next app open: it only `CREATE`s a table, touching no existing row).
- Full unit suite green (reported with the commit); render smoke green (constructs the new window with no missing resource); engine diff across the phase empty.

Local commit only; no push/tag/version.json; 1.41.0 unannounced.
