# 09 — تحقّق لوحة التحكم (S49)

> كل مؤشّر مُعاد حسابه باستعلام SQL مستقلّ على نفس القاعدة في نفس اللحظة، ومقارن بناتج
> `DashboardService.BuildOverview/BuildWorkforce`. الدليل: `AuditDashboardVerify.EveryDashboardIndicator_EqualsIndependentSql` (أخضر).
> البيانات المبذورة: 8 موظفين — 4 IT نشطون، 3 RH نشطون، 1 RH خرج (غير نشط).

| المؤشّر | قيمة البرنامج | الاستعلام المستقل | نصّ الاستعلام |
|---|---|---|---|
| Effectif (headcount نشط) | 7 | 7 | `SELECT COUNT(*) FROM Employees WHERE CompanyId=? AND IsDeleted=0 AND IsActive=1` |
| Masse salariale | 412 000 | 412 000 | `SELECT SUM(CAST(BaseSalary AS REAL)) … IsActive=1` |
| Salaire moyen | 58 857 | 58 857,14 (exact) | `masse / effectif` ; البرنامج **يقرّب إلى الدينار** (58 857) — فرق تقريب عرض لا خطأ بيانات |
| Par département — IT | 4 | 4 | `… AND Department='IT'` |
| Par département — RH | 3 | 3 | `… AND Department='RH'` |
| Σ ByDepartment | 7 | 7 (= effectif) | مجموع كل الخانات = الإجمالي النشط (لا سقوط/ازدواج) |
| Σ ByGender | 7 | 7 (= effectif) | مجموع كل الخانات = الإجمالي النشط |

النتيجة: **تطابق تام** على كل مؤشّر قابل للمقارنة. الموظف الخارج (غير النشط) **مُستبعَد** صحيحاً من الإحصاء النشط. كل توزيع (قسم/جنس) **يقسّم كامل الإجمالي النشط** دون نقص أو ازدواج.

ملاحظة واحدة (لا عيب): **Salaire moyen** يُعرَض مقرّباً إلى الدينار (58 857)، بينما القيمة الدقيقة 58 857,14. اختيار عرض، مطابق للإجمالي المستقلّ عند الأرقام الصحيحة. الاختبار يؤكّد التطابق « إلى الدينار ».

> مؤشّرات أخرى في اللوحة (contrats expirant، congés en attente، prêts، présence du jour، turnover، proche-retraite) لكلٍّ منها اختبار وحدة مخصّص في حزمة `DashboardService` (ضمن 1755 الخضراء)؛ هنا تحقّقنا من المؤشّرات الهيكلية (effectif، masse، moyenne، répartitions) مقابل SQL مستقلّ مباشرةً كما طلب S49.
