# OptiPaie DZ — Module Specifications

**Single source of truth for every current and future module.** Architecture
only — no module here is implemented yet (except Payroll). Every future module
MUST follow this exact standard so it integrates cleanly into the licensing
architecture and the shared local database.

Related documents:
- `backend/MODULES.md` — canonical module keys (cloud ↔ admin ↔ desktop).
- `backend/README.md` — licensing platform (Supabase, signed tokens).
- Licensing keys are locked: never rename a `ModuleKey` once shipped.

---

## 1. Global integration rules (apply to ALL modules)

1. **One shared database.** Every module reads and writes the **existing local
   SQLite database** (`optipaie.db`). There is exactly one `Employees` table, one
   `Companies` table, one Payroll dataset — shared by all modules. No module has
   its own database, and no module duplicates business data.
2. **No migration on unlock.** Enabling a module only flips a license permission.
   It never imports, migrates or copies data. A module's own tables (if any) are
   created once by an **additive** numbered migration (`0006+`), independently of
   the license state, and simply sit empty until the module is used.
3. **Shared entities are canonical.** Modules reference employees and companies by
   foreign key to `Employees(Id)` and `Companies(Id)` — never by copying rows.
4. **Permission = license module flag.** A module is available only when its
   `ModuleKey` appears in the signed license token's `modules[]`. The app is
   single-user, so there is no per-user RBAC; the license flag *is* the
   permission. Locked modules stay visible in the nav with a 🔒 and an upsell.
5. **Registry-driven.** Every module is declared once in the desktop
   `IModuleRegistry` with the same key as the cloud. Nav, gating and lock state
   are generated from the registry — adding a module never requires refactoring
   the shell.
6. **Additive migrations only.** New tables/columns are added; existing Payroll
   schema is never altered destructively. Backward compatibility is mandatory.

### Existing shared tables (already in the database)
`Companies`, `Employees`, `PayrollElements`, `EmployeeElements`, `PayrollRuns`,
`Payslips`, `PayrollDetails`, `ArchiveDocuments`, `LegalParameters`,
`AppSettings`, `Languages`, `BackupRecords`.

`Employees` key columns reused everywhere: `Id`, `CompanyId`, `LastNameFr/Ar`,
`FirstNameFr/Ar`, `Nss`, `NationalId`, `HireDate`, `ExitDate`, `Category`,
`Poste`, `ContractType`, `BaseSalary`, `IsActive`.

### Future-table naming convention
`{Domain}` PascalCase, employee link = `EmployeeId INTEGER REFERENCES Employees(Id)`,
company link = `CompanyId INTEGER REFERENCES Companies(Id)`, soft-delete
`IsDeleted`, audit `CreatedAtUtc`/`UpdatedAtUtc` — mirroring the existing schema
style (money/dates stored as invariant TEXT).

### Specification template (copy for every new module)
```
### <n>. <Display Name>
- ModuleKey:            <key>
- Display Name:         FR "…" / AR "…" / EN "…"
- Description:          …
- Purpose:              …
- Navigation Location:  …
- Required Permissions: license module <key> enabled (+ any dependency)
- Dependencies:         <none | other ModuleKeys | core entities>
- Future Screens:       … (placeholders)
- Database Tables Used: <existing shared> + <new module tables>
- Shared Entities:      Employees / Companies / Payroll (read / write)
```

---

## 2. Module specifications

### 1. Payroll — `payroll` (CORE, always enabled)
- **Display Name:** FR "Paie" / AR "الأجور" / EN "Payroll"
- **Description:** Algerian payroll engine and payslip production (CNAS, IRG,
  abattement, lissage) per in-force law.
- **Purpose:** The base product every customer buys; computes and archives
  salaries and produces the Fiche de paie.
- **Navigation Location:** Left nav rail → **Paie** (+ Accueil / Employés /
  Entreprises / Archive / Paramètres core screens).
- **Required Permissions:** Always enabled (core). Present in every active license.
- **Dependencies:** None. It is the foundation the other modules extend.
- **Future Screens:** *(already implemented)* Payroll worksheet, catalogue of
  payroll items, Fiche de paie (QuestPDF), Archive.
- **Database Tables Used:** `Employees`, `Companies`, `PayrollElements`,
  `EmployeeElements`, `PayrollRuns`, `Payslips`, `PayrollDetails`,
  `LegalParameters`, `ArchiveDocuments`.
- **Shared Entities:** Employees (read), Companies (read), Payroll (read/write).

### 2. ATS / DRT — `ats`
- **Display Name:** FR "ATS / DRT" / AR "ATS / DRT" / EN "ATS / DRT documents"
- **Description:** Automatic generation and printing of ATS / DRT declarations
  (worker-movement / statutory declaration documents) from existing employee and
  company data. *(Exact declaration set to be confirmed with the owner.)*
- **Purpose:** Produce required official declarations without re-entering data.
- **Navigation Location:** Left nav rail → **ATS / DRT** (🔒 when locked).
- **Required Permissions:** license module `ats` enabled.
- **Dependencies:** Reads core `Employees` + `Companies`; may read Payroll
  results for declaration figures.
- **Future Screens:** Declaration list, declaration builder (period + employee
  selection), print/PDF preview, template settings.
- **Database Tables Used:** `Employees`, `Companies`, `Payslips` (read) +
  **new:** `AtsDrtDocuments`, `AtsDrtDocumentLines`.
- **Shared Entities:** Employees (read), Companies (read), Payroll (read).

### 3. Attendance — `attendance`
- **Display Name:** FR "Gestion du pointage" / AR "إدارة الحضور" / EN "Attendance"
- **Description:** Record and manage employee attendance, absences, worked hours
  and overtime by period.
- **Purpose:** Track presence and feed worked/overtime hours into payroll.
- **Navigation Location:** Left nav rail → **Pointage** (🔒 when locked).
- **Required Permissions:** license module `attendance` enabled.
- **Dependencies:** Core `Employees`. **Optional integration → `payroll`**
  (worked/overtime hours can flow into the payroll worksheet's WorkedHours).
- **Future Screens:** Monthly attendance grid, daily entry, overtime summary,
  import from device (future), period lock.
- **Database Tables Used:** `Employees` + **new:** `AttendancePeriods`,
  `AttendanceRecords`, `AttendanceSummaries`.
- **Shared Entities:** Employees (read), Payroll (write-through hours, optional).

### 4. Leave & Vacation — `leave`
- **Display Name:** FR "Gestion des congés" / AR "إدارة العطل" / EN "Leave"
- **Description:** Manage leave types, requests, approvals and running balances
  (annual, sick, unpaid, etc.).
- **Purpose:** Track entitlements and consumed leave per employee.
- **Navigation Location:** Left nav rail → **Congés** (🔒 when locked).
- **Required Permissions:** license module `leave` enabled.
- **Dependencies:** Core `Employees`. **Optional → `attendance`** (leave days
  reflect in attendance) and **`payroll`** (unpaid leave affecting salary).
- **Future Screens:** Leave requests list, request form, balance dashboard, leave
  types settings, calendar view.
- **Database Tables Used:** `Employees` + **new:** `LeaveTypes`,
  `LeaveRequests`, `LeaveBalances`.
- **Shared Entities:** Employees (read), Attendance/Payroll (read, optional).

### 5. Employee Loans & Advances — `loans`
- **Display Name:** FR "Prêts & avances" / AR "القروض والتسبيقات" / EN "Loans & advances"
- **Description:** Manage employee loans and salary advances with instalment
  schedules and outstanding balances.
- **Purpose:** Track amounts owed and recover them via payroll deductions.
- **Navigation Location:** Left nav rail → **Prêts** (🔒 when locked).
- **Required Permissions:** license module `loans` enabled.
- **Dependencies:** Core `Employees`. **Optional integration → `payroll`**
  (a due instalment can be posted as a retenue line on the payslip).
- **Future Screens:** Loans list, new loan/advance form, instalment schedule,
  balance per employee.
- **Database Tables Used:** `Employees` + **new:** `EmployeeLoans`,
  `LoanInstallments`.
- **Shared Entities:** Employees (read), Payroll (write deduction, optional).

### 6. Performance, Promotions & Position Changes — `performance`
- **Display Name:** FR "Évaluation & promotions" / AR "التقييم والترقيات" / EN "Performance & promotions"
- **Description:** Employee performance evaluations plus promotion and
  position/category change history.
- **Purpose:** Assess employees and record career/position/salary changes.
- **Navigation Location:** Left nav rail → **Évaluation** (🔒 when locked).
- **Required Permissions:** license module `performance` enabled.
- **Dependencies:** Core `Employees`. **Writes back to `Employees`** when a
  promotion changes `Poste` / `Category` / `BaseSalary` (via the employee service
  — never a duplicate record).
- **Future Screens:** Evaluations list, evaluation form (criteria + score),
  promotion/position-change wizard, employee career timeline.
- **Database Tables Used:** `Employees` (read/update) + **new:**
  `PerformanceCriteria`, `PerformanceReviews`, `EmployeePositionChanges`.
- **Shared Entities:** Employees (read/write), Companies (read).

### 7. Contracts Management & Renewal — `contracts`
- **Display Name:** FR "Contrats & renouvellements" / AR "العقود والتجديد" / EN "Contracts"
- **Description:** Manage employment contracts, terms, expiry and renewals with
  document generation.
- **Purpose:** Track contract lifecycles and produce/renew contract documents.
- **Navigation Location:** Left nav rail → **Contrats** (🔒 when locked).
- **Required Permissions:** license module `contracts` enabled.
- **Dependencies:** Core `Employees` (`ContractType`, `HireDate`, `ExitDate`) +
  `Companies`. May update employee contract fields on renewal.
- **Future Screens:** Contracts list, contract editor, renewal workflow, expiry
  alerts, contract templates, print/PDF.
- **Database Tables Used:** `Employees` (read/update), `Companies` (read) +
  **new:** `EmployeeContracts`, `ContractRenewals`, `ContractTemplates`.
- **Shared Entities:** Employees (read/write), Companies (read).

### 8. Training & Courses — `training`
- **Display Name:** FR "Formation & cours" / AR "التكوين والدورات" / EN "Training"
- **Description:** Manage training courses, sessions and employee enrolments with
  completion tracking.
- **Purpose:** Plan and record employee training and certifications.
- **Navigation Location:** Left nav rail → **Formation** (🔒 when locked).
- **Required Permissions:** license module `training` enabled.
- **Dependencies:** Core `Employees` + `Companies`.
- **Future Screens:** Courses catalogue, session planner, enrolment management,
  per-employee training history, certificate print.
- **Database Tables Used:** `Employees` (read), `Companies` (read) + **new:**
  `TrainingCourses`, `TrainingSessions`, `TrainingEnrollments`.
- **Shared Entities:** Employees (read), Companies (read).

### 9. Assets & Assigned Equipment — `assets`
- **Display Name:** FR "Biens & équipements" / AR "الأصول والمعدات" / EN "Assets & equipment"
- **Description:** Track company assets/equipment and their assignment to
  employees (issue, return, condition).
- **Purpose:** Know which equipment each employee holds and its status.
- **Navigation Location:** Left nav rail → **Biens** (🔒 when locked).
- **Required Permissions:** license module `assets` enabled.
- **Dependencies:** Core `Employees` + `Companies`.
- **Future Screens:** Asset register, asset categories, assignment (issue/return)
  form, per-employee assigned-equipment sheet, print handover slip.
- **Database Tables Used:** `Companies` (read), `Employees` (read) + **new:**
  `AssetCategories`, `Assets`, `AssetAssignments`.
- **Shared Entities:** Companies (read), Employees (read).

### 10. Work Certificate — `work_certificate`
- **Display Name:** FR "Attestation de travail" / AR "شهادة العمل" / EN "Work certificate"
- **Description:** Generate and print work/employment certificates from existing
  employee and company data.
- **Purpose:** Produce official attestations on demand with correct legal wording.
- **Navigation Location:** Left nav rail → **Attestation** (🔒 when locked), and
  as a quick action from an employee profile.
- **Required Permissions:** license module `work_certificate` enabled.
- **Dependencies:** Core `Employees` + `Companies` only. No new business data
  required — purely a document generator.
- **Future Screens:** Certificate builder (employee + type selection),
  print/PDF preview, template & wording settings, issued-certificates log.
- **Database Tables Used:** `Employees` (read), `Companies` (read) + **new
  (optional):** `WorkCertificateTemplates`, `WorkCertificateLog`.
- **Shared Entities:** Employees (read), Companies (read).

---

## 3. Dependency summary

| Module | Reads Employees | Reads Companies | Payroll integration | Writes Employees |
|--------|:---:|:---:|:---:|:---:|
| `payroll` (core) | ✔ | ✔ | — (is payroll) | — |
| `ats` | ✔ | ✔ | reads figures | — |
| `attendance` | ✔ | — | feeds hours (opt.) | — |
| `leave` | ✔ | — | unpaid leave (opt.) | — |
| `loans` | ✔ | — | posts deduction (opt.) | — |
| `performance` | ✔ | ✔ | — | ✔ (promotion) |
| `contracts` | ✔ | ✔ | — | ✔ (renewal fields) |
| `training` | ✔ | ✔ | — | — |
| `assets` | ✔ | ✔ | — | — |
| `work_certificate` | ✔ | ✔ | — | — |

All "Payroll integration" points are **optional, one-directional and additive** —
a module contributes a line/value into the existing payroll flow through the
established services; it never forks the payroll engine or its data.

---

## 4. What this guarantees
- Every module — today's and tomorrow's — is documented to the **same standard**.
- Each has a unique `ModuleKey`, a nav location, explicit dependencies, and a
  clear list of shared vs. new tables **before** any code is written.
- The licensing architecture is ready for all of them: cloud registry, admin
  toggles, signed-token gating and desktop 🔒 states already exist.
- Unlocking any module changes **only UI + permissions** — never the customer's
  business data.
