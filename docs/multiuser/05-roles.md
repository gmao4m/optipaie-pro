# 05 — المستخدمون والأدوار

## 5.1 — هل يوجد نظام مستخدمين اليوم؟ **نعم، نظام جزئي يعمل (دورَان). ليس من الصفر.**
خلافًا للفرضية، البنية موجودة ودورتها كاملة — لكنها **اختيارية ومطفأة افتراضيًّا** وبدورين فقط:
- جدول `Users` (اسم، هاش PBKDF2، ملح، دور، قسم) — [`0023_Users.sql`](../../src/OptiPaie.Data/Sql/Migrations/0023_Users.sql).
- خدمة كاملة: إنشاء/تعديل/حذف/تغيير كلمة سرّ/مصادقة + حارس «آخر مدير» — [`UserService.cs`](../../src/OptiPaie.Services/UserService.cs).
- **واجهة إدارة مستخدمين موجودة**: [`UsersViewModel.cs`](../../src/OptiPaie.Desktop/ViewModels/UsersViewModel.cs) + `Views/UsersWindow.xaml`.
- بوّابة دخول موحّدة (مستخدم محلّي أو بريد المالك) — [`LoginCoordinator.cs`](../../src/OptiPaie.Services/Auth/LoginCoordinator.cs)، مطفأة عبر `Auth.Enabled` ([`UserService.cs:220`](../../src/OptiPaie.Services/UserService.cs)).
- **الدوران الحاليّان فقط:** `Admin=1`, `Manager=2` ([`UserRole.cs`](../../src/OptiPaie.Core/Enums/UserRole.cs)، [`0023:16`](../../src/OptiPaie.Data/Sql/Migrations/0023_Users.sql)).

**الأثر على التقدير:** الأدوار الخمسة **توسيع** لنظام قائم (تعداد 2→5 + فرض في الخدمة + مصفوفة)، لا بناء جديد. وجدول `Users` **بلا CHECK على `Role`** → التوسيع لا يصطدم بقيد ([3.4](03-db-gap.md)).

## 5.2 — الأدوار الخمسة ومصفوفة الصلاحيات

الرموز: **V** = يرى/يفتح · **E** = يعدّل/ينشئ/يحذف · **P** = يطبع/يصدّر · «—» = لا وصول.

| الوحدة (مفتاح الشاشة) | مدير | موارد بشرية | مسيّر أجور | محاسب | اطّلاع |
|---|---|---|---|---|---|
| لوحة القيادة `dashboard` | V | V | V | V | V |
| الشركات `companies` | V E | — | — | V | — |
| الإعدادات `settings` | V E | — | — | — | — |
| المستخدمون `users` | V E | — | — | — | — |
| الموظّفون `employees` | V E P | V E P | V | V P | V P |
| العناصر/القواعد `payroll-elements` | V E | V | V E | V | V |
| الأجور/الكشوف `payroll` | V E P | V | V E P | V P | V P |
| تصاريح CNAS (DAC/DAS) `cnas` | V E P | — | V E P | V P | V P |
| الشهادات ATS/DRT `attestations` | V E P | V E P | V P | V P | V P |
| الحضور `attendance` | V E | V E | V E | V | V |
| العطل `leave` | V E P | V E P | V | V | V P |
| القروض/التسبيقات `loans` | V E | V | V E | V P | V |
| الأداء `performance` | V E P | V E P | — | — | V |
| العقود `contracts` | V E P | V E P | V | V P | V P |
| التكوين `training` | V E P | V E P | — | V | V |
| العتاد `assets` | V E | V E | — | V | V |
| شهادات العمل `work-certificate` | V E P | V E P | V P | V P | V P |
| التوظيف `recruitment` | V E P | V E P | — | — | V |
| الأرشيف `archive` | V P | V P | V P | V P | V P |
| التقارير `reports` | V P | V P | V P | V P | V P |

(المصفوفة اقتراح منطقي لمكتب أجور جزائري؛ قابلة للضبط معك قبل أي بناء. النقطة الثابتة: **لكلّ عملية إذنٌ معرّف**، والفحص في الخدمة.)

## 5.3 — أين يقع الفحص؟ **نقطة اختناق واحدة في طبقة الخدمات — مُنفَّذة.**
- **مُنفَّذ (المرحلة 1):** `AccessPolicy.Require(context, permission)` يُستدعى في مستهلّ كل عملية كتابة
  حسّاسة في الخدمات؛ `IAccessContext` يأتي من الجلسة (`SessionAccessContext`). **يفشل مغلقًا**: سياق
  غائب = رفض. استرجاع قاعدة البيانات **لمالك فقط** (`RequireOwner` + `IsOwner`).
- **الواجهة تتبع المصدر نفسه:** التنقّل يَظهر بندًا فقط إن كان `AccessPolicy.CanOpen(role, key)` صحيحًا —
  نفس المصدر، فلا يختلف المرئيّ عمّا تسمح به الخدمة.
- **اختبار لا يصدأ:** `WriteGuardReflectionTests` يعدّد كل خدمة ويثبت أن كل طريقة تعيد `Result` مرفوضة لعديم
  الصلاحية ما لم تُعلَّم `[WriteGuardExempt("سبب")]`؛ يفشل يوم تُضاف عملية غير محروسة.

## 5.4 — عزل الشركات مع مستخدمين مختلفين
- **عزل البيانات متين ويصمد:** كل استعلام أعمال مُقيَّد بـ`CompanyId` صراحةً عبر `CompanyContext.RequireActiveId()`
  ([`CompanyContext.cs:68`](../../src/OptiPaie.Desktop/Composition/CompanyContext.cs))؛ الشركة النشطة حالة لكل عميل، فلا تسرّب بين الأجهزة.
- **لكن لا ربط مستخدم↔شركة:** جدول `Users` **بلا عمود `CompanyId`** ولا جدول ربط (`UserCompany`: بحث فارغ).
  أي مستخدم داخل (أو في الوضع المفتوح) يستطيع التبديل إلى **أي** شركة من المحدِّد العلوي.
- **هل يمكن ربط مستخدم بشركة واحدة أو عدّة؟** ليس اليوم. يحتاج (أ) جدول ربط `UserCompanies(UserId, CompanyId)` + (ب) تقييد قائمة الشركات في [`CompanyContext.Reload`](../../src/OptiPaie.Desktop/Composition/CompanyContext.cs) بما يُسمح للمستخدم. إضافي، ولا يمسّ المحرّك. (لمكتب بشركة واحدة لا يلزم أصلًا — يؤجَّل، [وثيقة 08](08-out-of-scope.md).)

## 5.5 — كم وحدة يجب تعديلها لإدخال الفحص؟
- **نقطة الفرض = 1** (مُزخرِف/سياسة واحدة في جذر التركيب).
- **خريطة الصلاحيات تغطّي ~18 وحدة مستخدم** فوق **~22 خدمة أعمال** (من أصل 30 خدمة؛ تُستثنى بنى تحتية: `AuditService`، `BackupService`، `LicenseService`، `ConfigurationService`، `NotificationService`، `DashboardService` للقراءة، `ReportService` للقراءة).
- العمل الحقيقي ليس «تعديل 22 ملفًّا» بل **تعريف إذن لكل عملية** وتمريرها بالمُزخرِف الواحد — عمل تعريفي مركّز، لا جراحة موزّعة.
