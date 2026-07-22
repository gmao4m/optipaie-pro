# Cross-cutting modules — Dashboard, Reports Center, Notifications

Three modules that sit **on top of** the nine HR modules. All three are **read-only
aggregations** — they consume the existing module services, never run their own SQL, and
never touch the payroll engine. They share one trait: add a new data source by extending
one method, and it appears everywhere.

---

## 14 · Executive Dashboard (`dashboard`)

The landing screen. `DashboardService.Build(window)` rolls up every module company-wide:

- **KPIs:** head-count, present/on-leave/on-mission today, active & expiring contracts,
  pending leave, active loans + outstanding, open postings + candidates, assigned assets,
  upcoming trainings.
- **"À traiter"** — a single approvals queue (pending leave), each row navigating to its
  module.
- **"Échéances à venir"** — dated deadlines (contract expiries within the window).

`DashboardServiceTests` (4) seed one company across all modules and assert the roll-up,
the two queues, the expiry window, and that approving an item drops it.

## 13 · Reports Center (`reports`)

A cross-module report library. Every report is a uniform `ReportTable` (title, columns,
string rows, numeric-column hints), so the preview grid, the **PDF** (`ReportDocument`,
A4 landscape) and the **CSV** (Excel-friendly, UTF-8 BOM) all render the same way.

Reports: **Effectif (roster)**, **Turnover** (hires/exits per month + total), **Synthèse de
présence** (month), **Passif de congés**, **Encours des prêts**, **Conformité formation**,
**Inventaire du matériel**, **Entonnoir de recrutement**. Pick report + company + period →
Générer → export.

`ReportServiceTests` (7) assert each report's columns/rows shape and key values, and that an
unknown key returns an empty table rather than crashing.

## 12 · Notifications & Reminders (bell in the top bar)

`NotificationService.GetNotifications(window)` gathers time-sensitive alerts from the
modules into one ranked list (most urgent, then soonest):

- **Contract expiries** — severity escalates: ≤7 j Urgent, ≤15 j Warning, else Info.
- **Leave awaiting approval** — Warning.
- **Trainings starting within 7 days** — Info.

The shell header shows an **Alertes** bell with a red count badge; clicking opens a popup
list, and clicking an alert navigates to its module. The bell refreshes on every screen
change, so counts stay live. New sources are added by extending `Collect`.

`NotificationServiceTests` (4) assert urgent-contract surfacing, pending-leave + upcoming
training, urgent-first ranking, and a quiet list when nothing is due.

---

### Files

| Layer | Files |
|---|---|
| Core | `Dtos/DashboardSnapshot.cs`, `Dtos/ReportTable.cs`, `Dtos/Notification.cs`; `Interfaces/Services/IDashboardService.cs`, `IReportService.cs`, `INotificationService.cs` |
| Services | `DashboardService.cs`, `ReportService.cs`, `NotificationService.cs` |
| Desktop | `ViewModels/DashboardViewModel.cs`, `ReportsViewModel.cs`; `Views/DashboardView.xaml`, `ReportsView.xaml`; `Documents/ReportDocument.cs`; the header bell in `Shell/MainWindow.xaml` + `ShellViewModel.cs` |
| Tests | `DashboardServiceTests`, `ReportServiceTests`, `NotificationServiceTests` (15 total) |

Status: builds **0 errors / 0 warnings**; full suite **1355/1355**. Payroll engine
untouched. Verified by clean build + service tests; the WPF surfaces were not click-tested
in this environment.

### Roadmap (from the master spec, not yet built)
Employee 360-view, multi-branch/org-chart Company, and the horizontals — **RBAC**,
per-record **audit log**, **offline-sync conflict rules**, full **RTL mirroring**,
**dark mode**, keyboard shortcuts — remain a multi-release effort.
