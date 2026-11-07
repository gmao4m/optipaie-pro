# 06 — الشرائح والنِّسب (extraites du code / des données)

> استخراج فقط، بلا حكم على الصحّة. المصدر (ملف:سطر) مذكور لكل قيمة. المالك يؤكّد.

## IRG — الباريم الشهري (هامشي)
المصدر: `src/OptiPaie.PayrollEngine/Legal/BuiltInLegalProfileProvider.cs:52-60` (profil « DZ-2026 », CIDTA Art. 104 / LF 2022، `EffectiveFrom = 2022-01-01`). الحساب في `Legal/IrgCalculator.cs`.

| الشريحة (DA) | من | إلى | النسبة |
|---|---|---|---|
| 1 | 0 | 20 000 | 0 % |
| 2 | 20 000 | 40 000 | 23 % |
| 3 | 40 000 | 80 000 | 27 % |
| 4 | 80 000 | 160 000 | 30 % |
| 5 | 160 000 | 320 000 | 33 % |
| 6 | 320 000 | — (∞) | 35 % |

- **الإعفاء (exemption):** الوعاء ≤ **30 000 DA** ⇒ IRG = 0 (`ExemptionThreshold = 30000`, سطر 77؛ تطبيق `IrgCalculator.cs:31`). لا خصم ثابت صريح في الباريم (النموذج هامشي صرف).

## Abattement
المصدر: `BuiltInLegalProfileProvider.cs:62` → `new AbattementRule(0.40m, 1000m, 1500m)`؛ التطبيق `IrgCalculator.cs:94-119`.
- النسبة: **40 %** من IRG الخام.
- الحد الأدنى: **1 000 DA** · الحد الأقصى: **1 500 DA** · ولا يتجاوز IRG الخام نفسه.

## Lissage (تنعيم الدخل المنخفض)
المصدر: `BuiltInLegalProfileProvider.cs:64-70` (`SmoothingRule`)؛ التطبيق `IrgCalculator.cs:41-52`. الطريقة `LissageMethod.Differential`.
- النطاق: الوعاء الخاضع في **] 30 000 ; 35 000 ]**.
- الصيغة: `IRG = (après_abattement × 137/51) − (27925/8)`، بحدٍّ أدنى 0.
  (137/51 ≈ 2.686275 · 27925/8 = 3490.625)
- عناصر الأجر غير الشهرية (Rappel/Prime exceptionnelle) تُنعَّم عبر `Rules/LissageRule.cs` (مستبعدة من الوعاء الشهري العادي — `TaxableBaseRule.cs:24`).

## CNAS
المصدر: `src/OptiPaie.Data/Sql/Migrations/0002_SeedReferenceData.sql:37-40` (LegalParameters، `EffectiveFrom = 2026-01-01`)؛ التطبيق `PayrollEngine/Rules/CnasRule.cs:21-22` (`base cotisable × taux`).

| الاشتراك | النسبة | المصدر |
|---|---|---|
| CNAS — حصّة الأجير | **9 %** (0.09) | `0002...sql:38` |
| CNAS — حصّة المستخدم | **26 %** (0.26) | `0002...sql:39` |
| SNMG | **24 000 DA** | `0002...sql:40` |

- فقط حصّة الأجير (9 %) تُنقص من الصافي؛ حصّة المستخدم تُخزَّن للتصاريح (`CnasRule.cs`).
- **التقسيم الرسمي** (décret 94-187، عرض فقط، « à confirmer »): 25,5 % مستخدم / 9 % أجير — `CnasDeclarationService.cs:32-42`. الفجوة 26 % (مطبّق) مقابل 25,5 % (رسمي) = بند قرار المالك (انظر CNAS DAC).

## عناصر الأجر الافتراضية — cotisable / imposable
المصدر: `0002_SeedReferenceData.sql:51-102` (الأعمدة IsCnasApplicable, IsIrgApplicable, IsIncludedInGross).

| العنصر | النوع | cotisable | imposable | في الإجمالي |
|---|---|---|---|---|
| Indemnité d'Expérience (ancienneté) | Gain | نعم | نعم | نعم |
| Prime de Rendement | Gain | نعم | نعم | نعم |
| Prime de Responsabilité | Gain | نعم | نعم | نعم |
| Prime de Risque / Nuisance | Gain | نعم | نعم | نعم |
| Prime de Nuit | Gain | نعم | نعم | نعم |
| Heures Supplémentaires | Gain | نعم | نعم | نعم |
| Prime de Panier | Gain | **لا** | **لا** | نعم |
| Prime de Transport | Gain | **لا** | **لا** | نعم |
| Indemnité de Congé Payé | Gain | نعم | نعم | نعم |
| Rappel | Gain | نعم | نعم | نعم (lissage) |
| Prime Exceptionnelle | Gain | نعم | نعم | نعم (lissage) |
| Retenue Absence | Deduction | نعم | نعم | نعم (ينقص الوعاء) |
| Acompte | Deduction | **لا** | **لا** | **لا** (retenue nette) |
| Avance | Deduction | **لا** | **لا** | **لا** (retenue nette) |

> السلّم (order) للقواعد في المحرّك: ElementResolution(100) → Gross(200) → Cotisable(300) → CNAS(400) → Taxable(500) → IRG(600) → Lissage(700) → Net(800). التقريب: `ROUNDING_SCALE=2` (centime، AwayFromZero) — `0002...sql:30` + `RoundingPolicy.Centime`.
