# 08 — نصف اليوم: الأسئلة الثلاثة (بالدليل المطبوع)

> الدليل من `AuditRoundTwo.HalfDay_AttendanceSchema_And_WorkedHours_Printed` (أخضر) + تحليل المستهلكين بالملف/السطر.

## Q1 — تعريف جدول الحضور الحالي + القيد

`.schema AttendanceRecords` (من `sqlite_master` على قاعدة مُرحَّلة فعلياً، سطر `Status`):
```
Status  INTEGER NOT NULL DEFAULT 1 CHECK (Status IN (1, 2, 3, 4, 5, 6, 7))
WorkedHours  TEXT NOT NULL DEFAULT '0'
```
(1=Present 2=Absent 3=Leave 4=Holiday 5=Rest 6=Late 7=Mission — القيد يسمح 1..7).

**هل migration مطلوب؟** يعتمد على التصميم:
- **التصميم المعتمد** لنصف اليوم لا يضيف قيمة Status جديدة. نصف اليوم يُمثَّل بجعل `AttendanceSummary.AbsentDays` (قيمة **مشتقّة، غير مخزّنة** في الجدول) من نوع `decimal` بدل `int`، فيساهم نصف يوم بـ 0.5. القيد `Status IN (1..7)` **لا يُمسّ** ⇒ **لا migration مطلوب**.
- **لو** اختار المالك بدل ذلك حالة مستقلّة (Status = 8 « HalfDay »)، فالقيد الحالي `IN (1..7)` **يرفض 8** ⇒ عندئذٍ **migration مطلوب حتماً** (توسيع القيد، نمط إعادة بناء الجدول كما في 0020). القرار بيد المالك؛ التصميم المقترح لا يسلك هذا الطريق.

## Q2 — WorkedHours قبل/بعد (أرقام فعلية)

من استعلام فعلي بعد كتابة الحالات عبر `AttendanceService` (`StandardHours = 8`):
```
WorkedHours — Present day = 8
WorkedHours — Absent day  = 0
```
المرجع: `AttendanceService.ApplyStatusOnly` (`AttendanceService.cs:428`) — `WorkedHours = IsWorked(Status) ? StandardHours : 0`.

- **قبل** (يوم حاضر كامل): WorkedHours = **8**.
- **بعد** نصف يوم: الميزة **غير منفّذة في الكود الحالي** (تجميد العمل على الميزات أثناء التدقيق)، فلا قيمة « بعد » فعلية لأطبعها — هذا تصريح صريح لا تهرّب. التصميم المعتمد يضبط نصف-اليوم-الحاضر على WorkedHours = StandardHours/2 = **4**، ونصف يوم غياب يساهم 0.5 في AbsentDays. لا يصل كسر إلى DAS: ساعات DAS تُجمع ربعياً ثم تُدوَّر إلى عدد صحيح واحد (`CnasDeclarationService.cs:267`).

## Q3 — كل مستهلكي رصيد العطل / أيام العمل، وتعامله مع العشري

| المكان (ملف:سطر) | يقرأ/يكتب | تعامله مع 0.5 |
|---|---|---|
| `BatchPayrollService.cs:257` | `monthDays − AbsentDays` (بوابة التناسب، paie groupée) | **decimal-safe** (`workedDays` decimal) |
| `PayrollViewModel.cs:423` | `monthDays − AbsentDays` (paie individuelle، mirror) | **decimal-safe** |
| `AttendanceSummary.PaidDays` (`AttendanceSummary.cs:41-48`) | مشتقّ: `monthDays − AbsentDays` | **int حالياً** → عند اعتماد نصف اليوم يجب أن يصير decimal (عيب محتمل لو نُسي) |
| `AttendanceService.Summarise` (`:509`) | `summary.AbsentDays++` | int الآن → عند نصف اليوم يجب `+= 0.5m` (يتطلّب AbsentDays decimal) |
| `ReportService.cs:192` | `AbsentDays.ToString()` | عرض — آمن مع decimal |
| `AttendanceReportDocument.cs:76` | `PaidDays.ToString()` | عرض — آمن |
| `EmployeeProfileViewModel.cs:315` | `AbsentDays.ToString()` | عرض — آمن |
| `AttendanceMonthViewModel.cs:91,132-133` | `Sum(AbsentDays)` / `PaidDays.ToString()` | عرض/جمع — آمن |
| `AttendanceEmployeeDetailViewModel.cs:138` | `AbsentDays` (نصّ) | عرض — آمن |
| `AttendanceMonthWindow.xaml:61-62` | binding `PaidDays`/`AbsentDays` | عرض — آمن |
| الوثائق — ATS `MonthlyContribution.DaysWorked` (`CertificateModels.cs:70`) | `decimal?` بالفعل، يُطبع `"0.##"` | **يدعم 0.5** (« 29.5 ») |
| الفيش — `PayslipReport.cs:456` | `WorkedDays.ToString("0.##")` | **يدعم 0.5** |
| المحرّك — `PayrollContext.WorkedDays/WorkableDays` (`:20-23`) | `decimal` | **آمن** (التناسب decimal) |

**العيوب المحتملة عند التنفيذ (int → decimal المطلوب):** `AttendanceSummary.AbsentDays` و`PaidDays` و`Summarise.AbsentDays++`. كل تحويل عشري→int في مسار الأجر = عيب؛ لا يوجد أي تحويل int ضمنيّ على `WorkedDays`/`WorkableDays` في المحرّك اليوم (مؤكّد بـ grep، [01-static](01-static.md) §د). الوعاء الأحادي الذي يلزم توسيعه هو `AbsentDays` فقط — بلا migration.
