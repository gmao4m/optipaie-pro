# Module 6 — Matériel (Assets)

Premium module, module key `assets`. Sixth module of the HR ecosystem. It tracks company
assets (laptops, phones, vehicles, tenues, outillage) and their hand-over to employees.
Assets belong to a **Company**; each hand-over references a **shared employee**, so who
holds what is always the live employee record — never a copy.

---

## 1. What it does

| Capability | Where |
|---|---|
| Assets of a company with their current holder | `Matériel` screen |
| Create / edit an asset | `Nouveau matériel` dialog |
| Assign · Return | dialogs |
| Repair · Retire · Available (status) | action bar |
| Assignment history per asset | `Historique` dialog |
| Available / assigned / total-value KPIs | KPI strip |

## 2. Business rules (single source of truth)

All rules live in `OptiPaie.Services/AssetService.cs`.

- **One holder at a time.** Assigning an asset that is already out is refused; a partial
  unique index (`WHERE ReturnedDate IS NULL`) enforces at most one open assignment per
  asset in the database too.
- **Full history preserved.** Returning closes the open assignment (records the return
  date and condition); the row is kept, so the chain of holders is never lost. An asset
  can then be reassigned.
- **Status is derived from the lifecycle**: `Available → Assigned → Available`, plus
  `UnderRepair` / `Retired`. A status change (repair/retire/available) requires the asset
  to be returned first; a retired asset cannot be assigned.
- **Guards**: an assigned asset cannot be deleted or have its status changed; the return
  date cannot precede the assignment; the holder must be a real shared employee.

## 3. Cross-module data sharing

Assignments reference the shared `Employees` table by foreign key, and the holder name
shown everywhere (`AssetSummary.HolderName`, the history) is resolved from that record at
read time. `GetHeldByEmployee` lists everything an employee currently holds — useful, for
example, before a contract is terminated. No employee or company data is duplicated; the
payroll engine, licensing and module-activation systems are untouched.

## 4. Data model

Migration `src/OptiPaie.Data/Sql/Migrations/0015_Assets.sql` — additive only.

```
Assets
  Id INTEGER PK   CompanyId → FK Companies(Id)   -- company property
  Name   Category (1 Laptop, 2 Phone, 3 Vehicle, 4 Uniform, 5 Tool, 99 Other)
  Status (1 Available, 2 Assigned, 3 UnderRepair, 4 Retired)
  SerialNumber   PurchaseDate   PurchaseValue TEXT   Notes
  CreatedAtUtc / UpdatedAtUtc / IsDeleted

AssetAssignments
  Id INTEGER PK   AssetId → FK Assets(Id) ON DELETE CASCADE
  EmployeeId → FK Employees(Id)                 -- the SHARED employee table
  AssignedDate   ReturnedDate (null = still held)
  ConditionOut / ConditionIn   Notes   CreatedAtUtc / IsDeleted
  UNIQUE (AssetId) WHERE ReturnedDate IS NULL AND IsDeleted = 0   -- one holder at a time
```

Dates bind through `SqliteDate.Day`.

## 5. Files

| Layer | File |
|---|---|
| Core | `Enums/AssetCategory.cs`, `Enums/AssetStatus.cs`, `Entities/Asset.cs`, `Entities/AssetAssignment.cs`, `Dtos/AssetSummary.cs` |
| Core | `Interfaces/Repositories/IAssetRepository.cs`, `Interfaces/Services/IAssetService.cs` |
| Data | `Sql/Migrations/0015_Assets.sql`, `Repositories/AssetRepository.cs` |
| Services | `AssetService.cs` |
| Desktop | `ViewModels/AssetViewModel.cs`, `AssetDialogViewModels.cs` (edit/assign/return/history) |
| Desktop | `Views/AssetView.xaml`, `AssetEditWindow.xaml`, `AssetAssignWindow.xaml`, `AssetReturnWindow.xaml`, `AssetHistoryWindow.xaml` |
| Tests | `tests/OptiPaie.Tests/AssetServiceTests.cs` |

## 6. Tests

`AssetServiceTests` — 14 integration tests against a **real SQLite file**:

- creation defaults to Available; name required
- assign marks the asset held by the shared employee (name resolved from the record);
  double-assign refused; unknown employee refused
- return frees the asset; return when not assigned refused; reassign after return keeps
  the history (exactly one open assignment)
- status guards: repair/retire need a return first; a retired asset cannot be assigned
- delete guard on assigned, delete allowed on available
- `GetHeldByEmployee` lists current holdings; `GetByCompany` returns holders

Status: **14/14 passing**, full suite **1290/1290 passing**, `OptiPaie.Desktop` builds
0 errors / 0 warnings.
