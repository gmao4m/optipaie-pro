# Canonical Module Registry — Payroll product (`product = payroll`)

Single source of truth for module keys. **Every layer reuses these exact keys
verbatim** — cloud DB seed (`0001_licensing_schema.sql`), Admin Panel toggles,
the signed license token's `modules[]`, and the desktop `IModuleRegistry`
(Phase 2). Never rename a key once shipped (it would orphan issued licenses); to
retire one, disable it — don't delete.

All modules share the **same local SQLite database** (Employees, Companies,
Payroll, …). Enabling a module never duplicates, migrates or imports data — it
only flips a permission; the module reads/writes the existing tables directly.

| # | ModuleKey | Product feature | Core | Default |
|---|-----------|-----------------|------|---------|
| 1 | `payroll`          | Payroll (base product)                                        | ✅ Core | **Always enabled** |
| 2 | `ats`              | ATS / DRT — automatic generation & printing                   | — | Disabled |
| 3 | `attendance`       | Attendance management                                         | — | Disabled |
| 4 | `leave`            | Leave & vacation management                                   | — | Disabled |
| 5 | `loans`            | Employee loans & advances                                     | — | Disabled |
| 6 | `performance`      | Performance evaluation, promotions & position changes         | — | Disabled |
| 7 | `contracts`        | Employee contracts management & renewal                       | — | Disabled |
| 8 | `training`         | Employee training & courses management                        | — | Disabled |
| 9 | `assets`           | Employee assets & assigned equipment management               | — | Disabled |
| 10| `work_certificate` | Work certificate — generation & printing                      | — | Disabled |

## Rules
- **Core is implicit.** `resolveModules()` always includes every `is_core`
  module of the product when the license is `active`. A brand-new license needs
  **no** `license_modules` rows to have Payroll working — so every upsell is
  disabled by default automatically.
- **One-click enable.** Admin flips `license_modules.enabled = true` for a key →
  the customer's next `validate` (on app start / 24h sync) returns a token whose
  `modules[]` includes it → the desktop unlocks that nav item live. No reinstall,
  no update, no migration.
- **Locked = visible + 🔒.** When a key is absent from `modules[]`, the desktop
  shows the nav item with a lock icon and an upsell message; it never disappears.
- **Implicit desktop-core screens** (`companies`, `employees`, `archive`,
  `settings`) are handled by the desktop registry as always-available and are
  intentionally **not** license-controlled here.

## Adding a future upsell module
1. `INSERT` one row into `modules` (this product, new key, fr/ar names, sort_order).
2. Add the same key to the desktop `IModuleRegistry` + build its screen.
3. It appears automatically as an Admin toggle and as a 🔒 item until enabled.

No schema change, no migration, no new installer.
