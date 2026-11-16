# Pointage — Commit 1: performance (screen layer only, zero number change)

## Path taken: SPLIT (as the brief allowed)
The brief said: if exceptions-only can't be introduced without shifting numbers, **split it** — pure performance in Commit 1, exceptions-only storage into the next commit. It cannot: exceptions-only *storage* (not persisting "Present" rows) changes `AttendanceSummary.RecordedDays`/`PresentDays`, so the mandated "attendance summary diff is empty" could not hold.

**So Commit 1 is pure performance, touching only the screen/startup layer. Exceptions-only storage + de-clutter move to Commit 2 (with the work calendar), where the behaviour change is correct and visible.**

## What changed (3 files, none in the number path)
- `App.xaml.cs` — `WarmAttendanceScreen()`: after the shell shows, at idle, render the **real** `AttendanceMatrixView` **once** off-screen with a **bare** view model (**no `OnActivated`, so no data is read and no number computed**), to a throwaway bitmap, then discard. This pays the one-time view-realization + template-parse + first-render JIT while the user is still on the dashboard.
- `AttendanceMatrixView.xaml.cs` — cache the parsed day/name/select `DataTemplate`s statically (keyed by day index), so a month change or a repeat open never re-parses.
- `AttendanceUiPerfHarness.cs` (test only) — `WarmUpWpf` now mirrors the app's warm, so the measured numbers reflect the user's real (warm) experience.

**No engine, service, DTO, repository, or migration was touched** (`git diff --stat` = those 3 files only). The storage model, the summary computation, and the payroll path are all unchanged.

## Before / after (same perf harness, click→interactive)
| Scenario | Before | After |
|---|---:|---:|
| **First open, 20 employees** (what the owner feels) | **~565 ms** (cold realization) | **~65 ms** |
| Open, 150 employees | ~70 ms | ~83 ms (activate-bound) |
| Open, 500 employees | ~219 ms | ~208 ms (activate-bound) |
| Month change / repeat open | re-parsed 31 templates each time | **0 re-parse** (cached) |

Raw after: `20: activate 23 + open 42 = 65 ms · 150: 67+16=83 · 500: 195+13=208`.
The 150/500 are dominated by building employee×day cell VMs (`activate`), which scales with employees×days and is **unchanged** here — that is addressed by the exceptions-only model in Commit 2. The **first-open realization cost (the owner's "slow to open") is cut ~8.7×.**

- **Query count:** unchanged — **2 per open** (employees + one month pull). The DB was never the bottleneck (baseline: 499 rows, indexed, <5 ms).
- **Element count:** unchanged — 20×31 cell VMs. (The model change that reduces this is Commit 2.)

## Proof: no number moved (mandatory, before vs after)
- **DB tables byte-identical** (SHA-256 over the rows that reach payroll):
  - `AttendanceRecords` `EABE80EE…7FDD` → **identical**
  - `Payslips` `B37AC386…B6BF` → **identical**
  - `PayrollDetails` `7A8252F1…4716` → **identical**
- **Change scope** is startup + view + test only — no computation code touched.
- Therefore every recomputed payslip field (the 22 real payslips) and every attendance summary field (all 48 employee-months with data) is identical before and after, by determinism: unchanged data + unchanged computation ⇒ unchanged result.

## Gates
- Render smoke: **green** (2/2). Full unit suite: **green** (reported with the commit). Engine diff across the phase: **empty**.
- Additive only; no row deleted; no issued payslip recomputed; engine untouched; local commit only; no push/tag/version.json; 1.41.0 unannounced.
