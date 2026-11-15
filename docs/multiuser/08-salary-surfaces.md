# 08 — جرد أسطح الأجر / Inventaire des surfaces « salaire »

> **القاعدة الواحدة.** إذنٌ وحيد — `Permission.ViewSalary` (`AccessPolicy.CanSeeSalary`) — يحكم
> **رؤية أجر موظّف مُعيَّن عبر أيّ باب**. دورٌ لا يملكه (اطّلاع) لا يرى الأجر في أيّ سطح ولا بأيّ صيغة.
> الحارس الميكانيكيّ هو **اختبار** [`SalaryExposureSweepTests`](../../tests/OptiPaie.Tests/SalaryExposureSweepTests.cs)
> الذي يبذر أجرًا مميّزًا ويثبت غيابه من المحتوى المُنتَج فعلًا (خلايا التقارير، بايتات الملفّ المصدَّر،
> والنصّ المرسوم على كل وثيقة)، مع شاهد موجب، ويفشل يوم يُضاف تقرير جديد يكشف أجرًا.

> **La règle unique.** Une seule permission — `Permission.ViewSalary` — gouverne le fait de voir la
> paie d'un employé identifié, **par n'importe quelle porte**. Un rôle qui ne l'a pas (اطّلاع) ne voit
> le salaire **nulle part**. Le garde mécanique est le test, pas une fermeture à la main.

## الأسطح / Les surfaces

| السطح / Surface | ما يكشفه | ما يحرسه الآن | الإيداع |
|---|---|---|---|
| فيش الأجر `FicheService` (معاينة/طباعة/تصدير) | الأجر الكامل | نقطة اختناق واحدة: `CanOpen(payroll)` للعرض، `CanExportPayroll` للطبع/التصدير + الأرشيف فارغ لاطّلاع | C |
| شاشة الأجور `PayrollView` | أجور الكشوف | `CanOpen(payroll)` (اطّلاع مُستثنى) | C |
| الأرشيف `ArchiveService` | الكشوف | قوائم فارغة لاطّلاع، و`GetDocument` محروس بالتصدير | C |
| CNAS DAC/DAS + الملفّات `CnasDeclarationService` | الأساس الخاضع | `CanOpen(cnas)` + `RequireView`/`RequireExport` | C |
| شهادة ATS/DRT، صفحة 2 (`BuildContributionsFromPayroll`) | الأساس الخاضع شهريًّا | يقرأ عبر الأرشيف المحروس ⇒ فارغ لاطّلاع | C |
| قائمة الموظّفين + تفاصيلها `EmployeesView` | الأجر القاعدي | مُحوِّل `SalaryMask` (ViewSalary) | **F** |
| الملفّ 360° — تبويب العقود `EmployeeProfileViewModel` | أجر العقد | ViewSalary؛ تبويب الكشوف عبر الأرشيف المحروس | **F**/C |
| قائمة العقود `ContractViewModel` | أجر العقد | ViewSalary (`SalaryVisibility.Text`) | **F** |
| **PDF العقد** `ContractDocument` (المادة 5) | أجر العقد | ViewSalary — **يُنتَج بلا المبلغ** (مقنَّع)، لا يُحجَب | **F** |
| **PDF شهادة العمل/الأجر** `WorkCertificateDocument` | الأجر الشهري | ViewSalary — **يُنتَج بلا المبلغ** (مقنَّع) | **F** |
| **PDF تصفية العطل** `LeaveSettlementDocument` | الأجر الشهري، الأجر اليومي، المبلغ | ViewSalary — **يُنتَج بلا الأرقام** (مقنَّعة) | **F** |
| مركز التقارير — كشف التعداد `ReportService.Headcount` | عمود الأجر القاعدي | ViewSalary (`SalaryVisibility.Text`) | **F** |
| مركز التقارير — CACOBATPH DAS/DAC | أسس الأجر السنوية | محروس بعرض الأجور (payroll) أصلًا | C |
| **تصدير الموظّفين xlsx/csv** `EmployeeImportExportService` | عمود الأجر القاعدي | ViewSalary — **العمود مقنَّع** «—»، باقي الأعمدة سليمة | **F** |
| لوحة القيادة: الكتلة الأجرية/المتوسّط/المنحنى `DashboardService` | **مجاميع الشركة** (ليست فردًا مُعيَّنًا) | غير محروس عمدًا — مجموع لا يُحلّ إلى فرد | — |

## قرار كل وثيقة (F.2) / Comportement par document

لكلّ وثيقة يحتاجها دورٌ لا يرى الأجر، **تُنتَج بلا الرقم** بدل الحجب — لأنّ الوثيقة نفسها قد تلزم
(تواريخ، بنود) ولا داعي لحرمانه منها، فقط من المبلغ:
- **PDF العقد** → المادة 5 تعرض «—» بدل المبلغ؛ العقد يُنتَج كاملًا. (الأساس: عقد الشغل قد يُطبع لغير من يرى الأجر.)
- **شهادة العمل/الأجر** → حقل الأجر «—»؛ الشهادة تُنتَج. (شهادة الأجر لمن لا يرى الأجر تصدر مقنَّعة لا محجوبة.)
- **تصفية العطل** → الأرقام المالية «—»؛ الأيام تظهر.
- **تصدير الموظّفين** → عمود الأجر «—»، كل عمود آخر سليم؛ الملفّ يُنتَج.

Les documents sont **produits sans le montant** (masqué « — ») plutôt que bloqués, car le document
lui-même (dates, articles) peut être nécessaire — seul le chiffre est retiré.

## الإثبات / La preuve
- [`SalaryExposureSweepTests`](../../tests/OptiPaie.Tests/SalaryExposureSweepTests.cs) — الأسطح الخدمية
  (التقرير على الخلايا، التصدير على **بايتات الملفّ**، نصّ كلّ وثيقة)، بشاهد موجب، وحلقة انعكاسيّة على
  **كل** واصف تقرير (تفشل يوم يُضاف تقرير يكشف أجرًا).
- [`ContractSalaryMaskTests`](../../tests/OptiPaie.UiTests/ContractSalaryMaskTests.cs) — سطح سطح المكتب
  (PDF العقد): يُقنَّع بلا الإذن، يَظهر معه، ويُرسَم في الحالتين.
