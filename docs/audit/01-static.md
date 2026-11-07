# 01 — الفحص البنيوي الساكن (Static Audit)

> كل بند بالملف والسطر، بناءً على Grep فعلي على `src/`.

## أ) موارد XAML غير معرّفة (سبب انهيار 1.38.0)

**الحارس موجود وأخضر.** `RenderSmokeTests.EveryTopLevelViewConstructs_WithNoUnresolvedResource`
يُنشئ كل Window/UserControl في `OptiPaie.Desktop` مع قواميس الثيم الحقيقية ويرفض أي
`XamlParseException`/`ResourceReferenceKeyNotFoundException`.

الدليل (تشغيل فعلي):
```
Views constructed OK: 66 / 67  (skipped ctor-needs-runtime: 1 → MainWindow)
Passed!  Failed: 0, Passed: 2  (RenderSmokeTests)
```
+ `DarkTheme_PackUri_ResolvesWithoutAnEntryAssembly` يثبت أن pack URI للثيم الداكن يُحلّ بلا entry assembly.

> فجوة معروفة: `MainWindow` يُتخطّى لأنه يحتاج خدمات وقت التشغيل في المُنشئ، فلا يغطّيه اختبار الدخان. انظر 02-e2e (G2) — يحتاج فتحاً فعلياً للصدفة.

## ب) catch فارغ / يبتلع الاستثناء — **أُصلح (G3 = 0)، round 2**

كانت 4 مواضع (6 كتل) تبتلع الاستثناء دون تسجيل. أُضيف تسجيل في كلٍّ (دون تغيير أي سلوك — إضافة سطر log فقط):

| الملف:السطر | السياق | الإصلاح |
|---|---|---|
| `Desktop/App.xaml.cs:61-64` | ترقية TLS 1.2/1.3 عند الإقلاع | `CrashLog.Breadcrumb("TLS … non activable: …")` |
| `Admin/App.xaml.cs:19-20` | نفسه (أداة الأدمِن) | `Trace.WriteLine(…)` |
| `Admin/Api/AdminSession.cs:71` | حذف ملف جلسة أفضل-جهد | `Trace.WriteLine("AdminSession.Clear: …")` |
| `Services/Updates/InstallerLauncher.cs:130` | fallback مسار exe | `Trace.WriteLine("InstallerLauncher: …")` |

التحقق: إعادة مسح `catch\s*(\(…\))?\s*\{\s*\}` على `src/` ⇒ **تطابق واحد فقط = نصّ سكربت PowerShell مولّد** (`InstallerLauncher.cs:111`، ليس C#). أي **صفر catch فارغ C#**. لا catch فارغ في `OptiPaie.PayrollEngine` ولا في مسار الحساب/الحفظ.

## ج) `.Result` / `.Wait()` / `async void` خارج معالجات الأحداث

- `.Wait()`: **صفر**.
- `.Result`: كل النتائج إيجابيات كاذبة (خاصية `Result` على كيانات التكوين/enum `TrainingResult`) عدا `Admin/Api/SupabaseAdminClient.cs:200` (`t.Result` — استدعاء Task حاجب في أداة الأدمِن، خارج العميل).
- `async void`: الغالبية معالجات أحداث (`*_Click`, `*_KeyDown`, `OnStartup`, `ICommand.Execute`) أو نمط VM المعتمد (`Load()`/`Reload()`). المرشّحات للتحقق أنها تلتقط الاستثناءات داخلياً (وإلا تُسقط التطبيق):
  `Desktop/App.xaml.cs:693 RunUpdateCheck`, `Desktop/ViewModels/DashboardViewModel.cs:154 Reload`,
  `SettingsViewModel.cs:225`, `ActivationViewModel.cs:227`, `LoginViewModel.cs:141` (كلها فحص تحديث).

> لم أتمكّن من الجزم: هل كل `async void` من هذه يلفّ جسمه بـ try/catch؟ يحتاج قراءة كل واحد. مُدرَج للمراجعة. (لا دليل على سقوط فعلي؛ فحوص التحديث تجري في الخلفية.)

## د) `double`/`float` في سياق مالي

**صفر في المحرّك.** الظهور الوحيد في `OptiPaie.PayrollEngine` تعليق في `MoneyEngine.cs:8` ينصّ «never float/double».
في `OptiPaie.Services`: 5 ملفات بها `double`، كلها **هندسة/زمن لا مال**:
- `AttendanceService` — ساعات/دقائق (`TotalHours`) ثم تحويل إلى decimal.
- `AtsDrtDocumentService`, `Certificates/AtsDrtFormRenderer`, `Documents/LeaveDocuments` — إحداثيات mm للطباعة.
- `DashboardService.cs:320,332` — `SeniorityYears`/`SeniorityBandKey` (سنوات العمر/الأقدمية، عدد لا عملة). كتلة الأجور تبقى decimal.

خلاصة G4: لا `double`/`float` في أي حساب مالي.

## هـ) مقارنة/تحويل حسّاس للثقافة في التصدير/الاستيراد/الطباعة

- `EmployeeImportExportService` يستعمل `CultureInfo.InvariantCulture` (حقل `Inv`, سطر 26)؛ استدعاءات `.ToString()` فيه على `StringBuilder` لبناء حقول CSV لا لتنسيق أرقام؛ الملف UTF-8+BOM (سطر 457). آمن ثقافياً.
- `DasEncoder.cs` يستعمل `CultureInfo.InvariantCulture` و ASCII صارم (يرفض غير ASCII) — `DasFormat` سطر 70+.
- تخزين القاعدة decimal كنصّ بثقافة ثابتة (عهدة 0001) + `SqliteTypeHandlers`.

> لم أتمكّن من مسح **كل** `.ToString()`/`Parse` في كامل الواجهة بحثاً عن ثقافة ضمنية؛ ركّزت على مسارات التصدير/الاستيراد/التصريح/الطباعة (الأعلى خطراً) وهي سليمة.

## و) IDisposable غير متخلّص منه

الاتصالات تُفتح عبر `SqliteConnectionFactory.CreateOpenConnection()` داخل `using` في `UnitOfWork`،
`MigrationRunner`، `SqliteBackupProvider`، واختبارات التدقيق. لم أرصد اتصال قاعدة مفتوحاً بلا `using`
في الطبقات المفحوصة.

> لم أتمكّن من ضمان 100% لكل IDisposable في الواجهة (تدفّقات PDF/صور SkiaSharp)؛ يحتاج فحص P6 (تسرّب الذاكرة بفتح/إغلاق 50 مرة) في بيئة تشغيل — مُدرَج في 04-perf.

## ز) N+1

أُصلح على هذا الفرع: `BatchPayrollService` يجلب ملخّص حضور الشركة باستعلام واحد (cache)،
و`DashboardService` موحّد (SQL تجميعي). الحضور يُحمَّل باستعلام `GetCompanyMonth` واحد.
(مفصّل في 04-perf + سجلّ pointage.)

## ح) نصوص ظاهرة مكتوبة مباشرة (hardcoded)

رُصد نصّ فرنسي مباشر في بعض الملاحظات/الرسائل (مثل `AttendanceNote` في `PayrollViewModel.cs:425`،
رسائل `AttendanceService`، عناوين أعمدة في code-behind). التعريب الرئيسي عبر `Strings.resx`/`Strings.ar.resx`
و`TranslationSource`. هذه بقايا سلاسل غير محوّلة.

> يحتاج قرار من المالك: حصر كل سلسلة مباشرة وتحويلها عمل تعريب موسّع، ليس إصلاح عيب. أُدرج كقائمة تحسين لا FAIL.
