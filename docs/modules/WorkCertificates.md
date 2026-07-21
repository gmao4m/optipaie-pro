# Module 9 — Attestations (Work Certificates)

Premium module, module key `work_certificate`. Ninth and final module of the HR
ecosystem. It issues HR documents — attestation de travail, certificat de travail,
attestation de salaire, and free documents — for employees. Only the issue metadata is
stored; the **body is rendered live from the shared employee and company records**, so an
attestation always reflects the current data (poste, salaire, ancienneté).

---

## 1. What it does

| Capability | Where |
|---|---|
| Certificates issued in a company | `Attestations` screen |
| Create / edit a certificate (auto-numbered) | `Nouvelle attestation` dialog |
| Print to PDF (A4, FR) rendered live | `Imprimer` action |
| Delete | action bar |

Types: **Attestation de travail**, **Certificat de travail** (fin d'emploi), **Attestation
de salaire**, **Document libre** (free body text).

## 2. Business rules (single source of truth)

All rules live in `OptiPaie.Services/WorkCertificateService.cs`.

- **Auto-numbering**: a new certificate without a reference gets one per company and year,
  prefixed by type — `ATT-2026-0007` (travail), `CERT-…` (certificat), `SAL-…` (salaire),
  `DOC-…` (libre). An explicit reference is kept as-is.
- **Live render**: `BuildRender` assembles the employee, company and current base salary
  from the shared records at print time; **seniority** is computed from the shared hire
  date at the issue date. Nothing about the employee is stored on the certificate, so a
  later salary change (e.g. a new contract activated) is reflected automatically.
- A **Document libre** requires a body; the standard types ignore it (rendered from data).
- Validation: the employee must exist.

## 3. Cross-module data sharing

The whole point of the module is to consume the shared records: the certificate carries
only `EmployeeId` + metadata, and every printed value (name, NSS, poste, hire date, exit
date, salary, seniority, company header) is read live. Because Contracts syncs
`Employee.BaseSalary`/`Poste` and ATS creates the employee, an attestation printed after
those actions is automatically correct. The payroll engine, licensing and
module-activation systems are untouched.

## 4. Data model

Migration `src/OptiPaie.Data/Sql/Migrations/0018_WorkCertificates.sql` — additive only.

```
WorkCertificates
  Id INTEGER PK   EmployeeId → FK Employees(Id)   -- the SHARED employee table
  Type (1 WorkCertificate, 2 WorkExperience, 3 SalaryCertificate, 99 Custom)
  Reference   IssueDate   Purpose   Body (Custom only)
  CreatedAtUtc / UpdatedAtUtc / IsDeleted
```

No company column: a company's certificates come from joining `Employees`. Dates bind
through `SqliteDate.Day`.

## 5. Files

| Layer | File |
|---|---|
| Core | `Enums/CertificateType.cs`, `Entities/WorkCertificate.cs`, `Dtos/CertificateSummary.cs` |
| Core | `Interfaces/Repositories/IWorkCertificateRepository.cs`, `Interfaces/Services/IWorkCertificateService.cs` |
| Data | `Sql/Migrations/0018_WorkCertificates.sql`, `Repositories/WorkCertificateRepository.cs` |
| Services | `WorkCertificateService.cs` |
| Desktop | `ViewModels/CertificateViewModel.cs`, `CertificateEditViewModel.cs` |
| Desktop | `Views/CertificateView.xaml`, `CertificateEditWindow.xaml` |
| Desktop | `Documents/CertificateDocument.cs` (QuestPDF A4, one layout per type) |
| Tests | `tests/OptiPaie.Tests/WorkCertificateServiceTests.cs` |

## 6. Tests

`WorkCertificateServiceTests` — 9 integration tests against a **real SQLite file**:

- auto-numbering by type and a shared per-company yearly counter; explicit reference kept
- a Custom document without a body is rejected; unknown employee rejected
- **live render** pulls the shared employee/company; a later salary change is reflected in
  a re-render without touching the certificate
- seniority computed from the shared hire date (3 years 6 months worked example)
- company listing carries the shared name; delete removes the certificate

Status: **9/9 passing**, full suite **1324/1324 passing**, `OptiPaie.Desktop` builds
0 errors / 0 warnings.

---

## The ecosystem is complete

All nine premium HR modules are built, integrated, tested and documented:

1. [Attendance](Attendance.md) · 2. [Leave](Leave.md) · 3. [Loans](Loans.md) ·
4. [Contracts](Contracts.md) · 5. [Performance](Performance.md) · 6. [Assets](Assets.md) ·
7. [Training](Training.md) · 8. [Recruitment/ATS](Recruitment.md) ·
9. Attestations (this document)

Every module shares one database and one employee/company record; changes in one flow to
the others automatically (leave → attendance → payroll; contracts → employee → payroll;
ATS hire → employee → everything); and the Payroll engine, licensing system and
module-activation system were never modified.
