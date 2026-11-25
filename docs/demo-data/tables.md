# Removing the trial demo company — every table, what happens to it

Schema v45: 42 tables. The removal (`DemoDataRepository.DeleteCompanyAndDependents`) runs in **one
transaction**, after an **automatic full backup**, children before parents. `@c` is the demo company's id.
The schema-wide test `EveryTableThatReferencesACompanyOrAnEmployee_IsInTheDeletionPlan` fails if a future
table with a `CompanyId` or `EmployeeId` column is not in this plan.

## Cleaned (32 tables + 1 link)

| # | Table | Rows removed |
|---|-------|--------------|
| 1 | EvaluationScores | scores of the demo's evaluations |
| 2 | Evaluations | of demo employees, demo periods or demo templates |
| 3 | EvalCriteria | criteria of the demo's own templates |
| 4 | EvalTemplates | `CompanyId = @c` (global templates, `CompanyId NULL`, are kept) |
| 5 | EvalPeriods | `CompanyId = @c` |
| 6 | Interviews | of the demo's candidates |
| 7 | CandidateAttachments | of the demo's candidates |
| 8 | Candidates | candidates of the demo's job postings |
| 9 | JobPostings | `CompanyId = @c` |
| 10 | TrainingParticipants | of the demo's sessions or demo employees |
| 11 | TrainingSessions | `CompanyId = @c` |
| 12 | AssetAssignments | of the demo's assets or demo employees |
| 13 | Assets | `CompanyId = @c` |
| 14 | ArchiveDocuments | archived PDFs of the demo's payslips |
| 15 | PayrollDetails | lines of the demo's payslips |
| 16 | LoanRepayments | of the demo's loans or payslips |
| 17 | Payslips | of demo employees or demo payroll runs |
| 18 | PayrollRuns | `CompanyId = @c` |
| 19 | Loans | of demo employees |
| 20 | AttendanceRecords | of demo employees |
| 21 | LeaveRequests | of demo employees |
| 22 | EmploymentContracts | of demo employees |
| 23 | WorkCertificates | of demo employees |
| 24 | EmployeeElements | of demo employees |
| 25 | BehaviorLogs | `CompanyId = @c` or demo employees |
| 26 | Employees | `CompanyId = @c` |
| 27 | Departments | `CompanyId = @c` |
| 28 | Holidays | `CompanyId = @c` only (national holidays, `CompanyId NULL`, are kept) |
| 29 | LeaveTypes | `CompanyId = @c` only (the 12 global types, `CompanyId NULL`, are kept) |
| 30 | CompanyWorkCalendar | `CompanyId = @c` |
| 31 | AuditLog | the demo's journal, `CompanyId = @c` (legacy rows with `CompanyId NULL` are kept) |
| 32 | Companies | the demo company itself |
| + | Candidates.HiredEmployeeId | a candidate of ANOTHER company recorded as hired as a demo employee keeps its record; only the dangling link is set to NULL |

## Settings adjusted

| Setting | Change |
|---------|--------|
| `DEFAULT_COMPANY_ID` | if it pointed at the demo → the remaining company |
| `RECENT_COMPANY` (legacy list) | the demo's entry is removed |

## Deliberately untouched (no company data)

AppSettings (except the two keys above), BackupRecords (the pre-removal backup is recorded here),
Languages, LegalParameters, PayrollElements (global catalogue), LicenseModules, LicenseState,
SchemaMigrations, TrialState, Users.

## Proof on a copy of the real database

See `docs/licence-demo/proof-demo-real-db-copy.txt`. 643 rows were removed in 19 non-empty tables. After the
removal, 0 rows reference the demo and the orphan scan (declared foreign keys plus every `*Id` column checked
against its parent) finds 0 orphans. The customer company's rows are byte-identical, and all 12 global leave
types are kept.
