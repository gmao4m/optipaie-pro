# 03 — سلامة البيانات والترحيل (Data & Migrations)

> الدليل لكل بند: اسم اختبار آلي + مخرجاته، أو نتيجة PRAGMA فعلية، أو تحليل سطر-بسطر للهجرة.

## D3 — إضافية الهجرات (additive-only)

المنهج: بحث عن كل DDL هدّام في `src/OptiPaie.Data/Sql/Migrations/` عبر Grep
(`DROP COLUMN | ALTER COLUMN | DROP TABLE | DROP INDEX | RENAME COLUMN | DELETE FROM`).
النتيجة: **31 هجرة من أصل 36 لا تحتوي أي DDL هدّام** (إضافية بالدليل). الخمس المتبقية:

| الهجرة | العملية الهدّامة | الحكم | الدليل |
|---|---|---|---|
| `0009_AttendanceSoftDeleteIndex` | `DROP INDEX IF EXISTS` ثم إعادة الإنشاء | آمنة — إعادة بناء فهرس، لا بيانات | قراءة الملف |
| `0020_AttendanceMissionStatus` | `DROP TABLE AttendanceRecords` | **آمنة — نسخ قبل الحذف** (`INSERT INTO *_new SELECT * FROM old` ثم DROP ثم RENAME) | `0020...sql:31-40` |
| `0025_AssetSharedMode` | `DROP INDEX IF EXISTS` ثم إعادة الإنشاء | آمنة — إعادة بناء فهرس | قراءة الملف |
| `0028_PerformanceRebuild` | `DROP TABLE IF EXISTS` لـ 9 جداول تقييم، **بلا نسخ** | ⚠️ **فقدان بيانات محتمل** — انظر «يحتاج قرار من المالك» أدناه | `0028...sql:22-30` |
| `0036_PayslipStatus` | `DROP INDEX UX_Payslips_Run_Employee` ثم فهرس Active + `ADD COLUMN` | آمنة — تبديل فهرس (metadata) + أعمدة بـ DEFAULT؛ الصفوف القائمة تأخذ Status=1 | `0036...sql:14-27` |

خلاصة D3: لا حذف عمود، لا تغيير نوع في أي هجرة. حالة واحدة (0028) تمسح جداول وحدة التقييم القديمة دون ترحيل بياناتها.

## D2/D6 — ذرّية الهجرة ومقاومة الانقطاع

كل هجرة تعمل في **معاملتها الخاصة** مع `Rollback()` عند أي خطأ، فيبقى القرص عند آخر إصدار متّسق.
الدليل: `MigrationRunner.Apply()` (`src/OptiPaie.Data/Migrations/MigrationRunner.cs:86-118`) — `BeginTransaction` → تنفيذ SQL + تسجيل الإصدار → `Commit`، و`catch { Rollback(); throw; }`.
اختبار مانع: `AuditDataIntegrityTests.UpgradingExistingDatabase_WritesIntactPreMigrationBackup_BeforeApplying` يعيد تطبيق هجرة على مخطّط موجود، تفشل، ثم يؤكّد أن القاعدة والنسخة سليمتان.

## D4 — integrity_check / foreign_key_check

الدليل (اختبار آلي أُضيف):
```
AuditDataIntegrityTests.FreshDatabase_PassesIntegrityAndForeignKeyCheck_AndTakesNoNeedlessBackup
  PRAGMA integrity_check  = "ok"
  PRAGMA foreign_key_check = 0 صف
  النتيجة: Passed (3/3 في الصنف، 887 ms)
```
على قاعدة مُرحَّلة بالكامل (36 هجرة) من `DatabaseBootstrapper.Initialize()`.

> لم أتمكّن من التحقق — تشغيل `integrity_check` على نسخة **زبون حقيقية** (D1): لا تتوفّر لديّ نسخة قاعدة من تثبيت زبون في هذه البيئة. الاختبار أعلاه يثبت السلامة على قاعدة مولّدة من نفس الهجرات التي يراها الزبون.

## D7 — نسخة احتياطية تلقائية قبل كل هجرة (كان مفقوداً — أُضيف)

**العرَض / السبب الجذري:** `DatabaseBootstrapper.Initialize()` كان ينشئ *مجلّد* النسخ لكنه لا يأخذ أي نسخة من ملف القاعدة قبل `runner.Run()`. فأي هجرة هدّامة أو فاشلة (مثل 0028) تقع دون شبكة أمان.

**الإصلاح:** قبل تطبيق أي هجرة معلّقة على قاعدة **قائمة** (إصدار مطبّق > 0)، يأخذ البرنامج نسخة متّسقة عبر `PRAGMA wal_checkpoint(TRUNCATE)` + SQLite Online Backup API إلى `BackupDirectory/pre-migration-v{N}-{timestamp}.db`. تثبيت جديد (إصدار 0) لا يأخذ نسخة (لا شيء يُحمى). فشل النسخ **يوقف** التحديث برسالة واضحة بدل الترحيل دون حماية.
- `MigrationRunner.GetPendingVersions()` (جديد) — كشف المعلّق دون تطبيق.
- `DatabaseBootstrapper.BackupBeforeMigration(...)` (جديد).

**الاختبار المانع:**
```
AuditDataIntegrityTests.UpgradingExistingDatabase_WritesIntactPreMigrationBackup_BeforeApplying  → Passed
AuditDataIntegrityTests.GetPendingVersions_FreshReturnsAll_ThenEmptyAfterRun                      → Passed
AuditDataIntegrityTests.FreshDatabase_...AndTakesNoNeedlessBackup (تثبيت جديد = 0 نسخة)           → Passed
```

## D5 — السطور اليتيمة

السلسلة محميّة بنيوياً بـ FK: `Payslips→Runs` CASCADE، `ArchiveDocuments→Payslips` RESTRICT، `Employees→Companies` RESTRICT، و`PRAGMA foreign_keys=ON` في كل اتصال (`SqliteConnectionFactory:51,77`). `foreign_key_check` أعلاه = 0 صف ⇒ لا يتامى على قاعدة مولّدة.

> لم أتمكّن من التحقق — إحصاء اليتامى على **بيانات زبون حقيقية بحجم 200 موظف** (D5): يحتاج نسخة قاعدة زبون + تشغيل الاستعلامات عليها. مُدرَج ضمن ما يحتاج بيئة اختبار فعلية.

## يحتاج قرار من المالك

1. **هجرة 0028 (إعادة بناء التقييم) تمسح جداول التقييم القديمة دون ترحيل.** إن كان أي زبون قد أدخل تقييمات على الوحدة القديمة (0014/0022/0027) قبل نشر 0028، فقد ضاعت. لا أستطيع التحقق من وجود بيانات زبون فعلية في تلك الجداول من المستودع. الهجرة **منشورة تاريخياً** فلا يمكن تعديلها الآن دون هجرة جديدة؛ وتعديلها قد يكسر تثبيتات قائمة. نسخة D7 الاحتياطية تحمي من تكرار هذا النمط مستقبلاً. القرار المطلوب: هل كانت وحدة التقييم القديمة منشورة لدى زبائن ببيانات حقيقية؟
