# 00 — خريطة النظام (System Map)

> مرجع للأقسام التالية. أُنتج بقراءة الكود مباشرة (المصدر مذكور لكل بند).
> الفرع: `chore/e2e-audit`. لا نشر، لا release.

## 1. الوحدات الوظيفية وملفات الدخول

| الوحدة | طبقة الخدمة | ViewModel رئيسي / نقطة دخول |
|---|---|---|
| الشركات (Companies) | `CompanyService` | `CompanyProvider` (اختيار الشركة النشطة) |
| الموظفون (Employees) | `EmployeeService` | `EmployeeListViewModel`, `EmployeeEditViewModel` |
| استيراد/تصدير الموظفين | `ImportExport/EmployeeImportExportService` | `EmployeeImportViewModel` (+ نافذة ربط الأعمدة) |
| كتالوج عناصر الأجر | `PayrollElementService` | `PayrollElementsViewModel` |
| الأجور (فردي) | `PayrollService` + `OptiPaie.PayrollEngine` | `PayrollViewModel` |
| الأجور (جماعي) | `BatchPayrollService` | `PayrollViewModel` (Batch) |
| الأرشيف / التصحيح / الحذف | `ArchiveService`, `PayslipRevisionService` | `ArchiveViewModel`, `PayslipCorrectionViewModel` |
| الحضور (Pointage) | `AttendanceService` | `AttendanceMatrixViewModel`, `AttendanceMonthViewModel` |
| العطل (Congés) | `LeaveService` | `LeaveViewModel`, `LeaveSettingsViewModel` |
| السلف (Prêts/Acomptes) | `LoanService` | `LoanViewModel` |
| العقود | `ContractService` | `ContractViewModel` |
| CNAS (DAC/DAS) | `CnasDeclarationService` + `Services/Cnas/*` | `CnasDacViewModel`, `CnasDasViewModel`, `CnasReadinessViewModel` |
| الشهادات (ATS/DRT/عمل) | `AtsDrtDocumentService`, `CertificateService` | `AttestationsViewModel` |
| التقييم (Performance) | `EvaluationService` (بعد إعادة البناء 0028) | `PerformanceViewModel` |
| التكوين (Training) | `TrainingService` | `TrainingViewModel` |
| الأصول (Assets) | `AssetService` | `AssetsViewModel` |
| التوظيف (Recrutement) | `RecruitmentService` | `RecruitmentViewModel` |
| لوحة التحكم | `DashboardService` | `DashboardViewModel` |
| المستخدمون/الجلسة | `UserService`, `CustomerAccount` | `LoginViewModel`, `ActivationViewModel` |
| الترخيص | `OptiPaie.Core/Licensing/*`, `LicenseGate` | — |
| التحديث التلقائي | `Services/Updates/*` (`IReleaseChannel`, `IUpdateService`) | `UpdateWindow` |
| النسخ الاحتياطي | `Data/Backup/SqliteBackupProvider` | — |
| التدقيق (Audit log) | `AuditService`, `ActorProvider` | `AuditViewModel` (Admin) |

التركيب (DI): `src/OptiPaie.Desktop/Composition/CompositionRoot.cs` + `AppServices.cs`.
الإقلاع: `src/OptiPaie.Desktop/App.xaml.cs` → `DatabaseBootstrapper.Initialize()` → `ShowMainShell`.

## 2. جداول قاعدة البيانات (من `0001_InitialSchema.sql` + الهجرات)

العهدة (conventions، من رأس 0001): المبالغ والنِّسب تُخزَّن **TEXT** (decimal بثقافة ثابتة)؛ البوليان/enum **INTEGER** بقيد CHECK؛ الطوابع الزمنية **TEXT** ISO-8601 UTC؛ الحذف الناعم عبر `IsDeleted`.

الجداول الأساسية (0001) مع أهم القيود:

| الجدول | قيود / فهارس مميّزة |
|---|---|
| `Companies` | `IsDeleted CHECK(0,1)`؛ `Logo BLOB` |
| `Employees` | FK→Companies **ON DELETE RESTRICT**؛ `Gender CHECK(1,2)`، `ContractType CHECK(1,2,3,4,99)`، `MaritalStatus CHECK(1..4)`، `Dependents CHECK(>=0)`، `PaymentMode CHECK(1,2,3)`؛ **لا قيد UNIQUE على NSS/NIF** (مقصود — تحقّق عند الوثيقة فقط) |
| `PayrollElements` | `ElementType CHECK(1,2)`، `CalculationMethod CHECK(1..4)` |
| `EmployeeElements` | **UNIQUE(EmployeeId, ElementId)**؛ FK→Employees CASCADE، FK→Elements RESTRICT |
| `PayrollRuns` | **UNIQUE(CompanyId, PeriodYear, PeriodMonth)**؛ `PeriodMonth CHECK(1..12)`، `RunStatus CHECK(1..4)` |
| `Payslips` | **UNIQUE(RunId, EmployeeId)** → بعد 0036 صارت `UX_Payslips_Run_Employee_Active WHERE Status=1`؛ FK→Runs CASCADE، FK→Employees RESTRICT؛ كل المبالغ TEXT DEFAULT '0' |
| `PayrollDetails` | FK→Payslips CASCADE، FK→Elements RESTRICT |
| `ArchiveDocuments` | FK→Payslips **RESTRICT** (يحمي الأرشيف)؛ `PdfContent BLOB`، `Checksum` |
| `LegalParameters` | **UNIQUE(ParamKey, EffectiveFrom)** (قيم قانونية مؤرّخة) |
| `AppSettings`, `Languages`, `BackupRecords` | key/value، لغات، سجل النسخ |

الجداول المضافة بالهجرات: `AttendanceRecords` (0008، أُعيد بناؤها 0020)، `LeaveRequests`/`LeaveBalances`/`LeaveTypes` (0010,0031,0035)، `Loans`/`LoanRepayments` (0012)، `Contracts` (0013)، جداول التقييم (0014→أعيد بناؤها 0028)، `Assets`/`AssetAssignments` (0015,0025)، `Trainings` (0016)، ATS/الشهادات (0017,0018,0029)، `AuditLog` (0021,0034)، `Users` (0023)، CACOBATPH (0024)، `Departments` (0019,0026)، `Holidays` (0032)، التوظيف (0033)، `SchemaMigrations` (bookkeeping، يُنشئه `MigrationRunner`).

`AttendanceRecords` فهرس مميّز جزئي: **`UX_Attendance_Employee_Date ON (EmployeeId, WorkDate) WHERE IsDeleted=0`** (0009/0020).

## 3. نقاط الكتابة على القرص

| ما يُكتب | أين | كيف |
|---|---|---|
| قاعدة البيانات | `DataDirectory/optipaie.db` (+ `-wal`,`-shm`) | SQLite WAL، عبر `UnitOfWork`/Repositories، معاملات |
| PDF كشف الراتب المؤرشف | عمود `ArchiveDocuments.PdfContent` (BLOB داخل القاعدة) | `ArchiveService` |
| PDF كشف راتب للعرض/الطباعة | ملف مؤقّت/مسار مختار | QuestPDF `FichePaieDocument` |
| شهادات ATS/DRT | PDF على مسار مختار | `AtsDrtFormRenderer` (SkiaSharp، إحداثيات mm) |
| تصدير الموظفين | xlsx (ClosedXML) / csv (UTF-8+BOM) | `EmployeeImportExportService` |
| نسخة احتياطية | `BackupDirectory/*.db` | `SqliteBackupProvider` (Online Backup API) + **قبل كل هجرة** (D7، مُضاف في هذا التدقيق) |
| جلسة الحساب | ملف DPAPI في AppData | `CustomerAccount` / `AdminSession` |
| إعدادات التطبيق | `AppSettings` (قاعدة) | `AppSettingRepository` |

## 4. نقاط الحساب المالي (كل ضرب/قسمة/جمع لمبلغ)

كلها داخل `src/OptiPaie.PayrollEngine/` (المحرّك)، **بـ `decimal` حصراً** (`MoneyEngine` يوثّق «never float/double»):

| القاعدة | الملف | العملية |
|---|---|---|
| تناسب الأجر القاعدي | `Rules/ElementResolutionRule.cs:37` | `BaseSalary * WorkedDays / WorkableDays` |
| حساب عناصر الأجر | `ElementCalculation/*Strategy.cs` | ثابت / نسبة / كمية×سعر / قاعدة×نسبة |
| الإجمالي الخاضع (Brut) | `Rules/GrossSalaryRule.cs` | Σ البنود المدرجة في الإجمالي |
| القاعدة الخاضعة لـ CNAS | `Rules/CotisableBaseRule.cs` | Σ البنود cotisable |
| اقتطاع CNAS | `Rules/CnasRule.cs` | assiette × نسبة |
| القاعدة الخاضعة للضريبة | `Rules/TaxableBaseRule.cs` | Brut − CNAS − بنود معفاة |
| IRG + abattement | `Rules/IrgRule.cs`, `Legal/IrgCalculator.cs`, `Legal/AbattementRule.cs` | شرائح الباريم + تخفيض |
| التنعيم (Lissage) | `Rules/LissageRule.cs` | توزيع عنصر على المدة |
| الصافي | `Rules/NetSalaryRule.cs` | Brut − CNAS − IRG − اقتطاعات |
| التقريب | `Money/MoneyEngine.cs` | `Round(...)` سياسة واحدة |

خارج المحرّك (عرض/تجميع فقط، لا قاعدة قانونية):
- `CnasDacReport`: `Assiette × RateSalariale/Patronale` (عرض الـ DAC) — `CnasDacReport.cs:58-60`.
- `DasFormat.Amount`: مبلغ → سنتيم صحيح (`decimal.Round`, `DasEncoder.cs:73`).
- `DashboardService`: كتلة الأجور (Σ decimal)؛ العمر/الأقدمية بـ `double` **سنوات** (ليست مالاً) — `DashboardService.cs:320`.
