# 07 — إعادة الحساب اليدوي (S14)

> الحساب اليدوي مستقلّ تماماً، مشتقّ من الباريم الرسمي (LF 2022، [06-rates](06-rates.md))،
> لا منسوخ من مخرج البرنامج. ناتج البرنامج من المحرّك الحقيقي عبر
> `AuditManualRecalc.Print_Three_Payslips_For_Manual_Comparison` ([Explicit]، أخضر).
> النِّسب: CNAS 9 %/26 %، abattement 40 % [1000–1500]، exemption ≤30 000، lissage ]30000;35000].
> التقريب: centime (2 décimales). شهر كامل (لا proraction).

## الحالة أ — أجر قاعدي بسيط بلا علاوات (base = 50 000)
الخطوات اليدوية:
- Brut = 50 000
- Base cotisable = 50 000 → CNAS salarié = 50 000 × 9 % = **4 500** ; CNAS patronal = 50 000 × 26 % = 13 000
- Base imposable = 50 000 − 4 500 = 45 500
- IRG brut (hors tranche lissée, >35 000) : 20 000×23 % + 5 500×27 % = 4 600 + 1 485 = **6 085**
- Abattement = 40 %×6 085 = 2 434 → plafonné à **1 500**
- IRG = 6 085 − 1 500 = **4 585**
- Net = 50 000 − 4 500 − 4 585 = **40 915**

| الخطوة | يدوي | البرنامج | الفرق |
|---|---|---|---|
| Salaire brut | 50 000.00 | 50 000.00 | 0 |
| Base cotisable | 50 000.00 | 50 000.00 | 0 |
| CNAS salarié | 4 500.00 | 4 500.00 | 0 |
| CNAS patronal | 13 000.00 | 13 000.00 | 0 |
| Base imposable | 45 500.00 | 45 500.00 | 0 |
| IRG brut | 6 085.00 | 6 085.00 | 0 |
| Abattement | 1 500.00 | 1 500.00 | 0 |
| IRG | 4 585.00 | 4 585.00 | 0 |
| **Net** | **40 915.00** | **40 915.00** | **0** |

## الحالة ب — علاوة cotisable (%) + علاوة non-cotisable + acompte (base = 40 000)
المدخل: Prime de Rendement 10 % (= 4 000، cotisable+imposable)، Prime de Transport 3 000 (non cotisable/non imposable، dans le brut)، Acompte 5 000 (retenue nette).
- Brut = 40 000 + 4 000 + 3 000 = 47 000 (l'acompte n'est pas dans le brut)
- Base cotisable = 40 000 + 4 000 = 44 000 (transport exclu) → CNAS salarié = 44 000×9 % = **3 960** ; patronal = 44 000×26 % = 11 440
- Base imposable = (40 000 + 4 000) − 3 960 = 40 040 (transport exclu de l'IRG)
- IRG brut : 20 000×23 % + 40×27 % = 4 600 + 10,80 = **4 610,80**
- Abattement = 40 %×4 610,80 = 1 844,32 → plafonné à **1 500**
- IRG = 4 610,80 − 1 500 = **3 110,80**
- Net = 47 000 − 3 960 − 3 110,80 − 5 000 (acompte) = **34 929,20**

| الخطوة | يدوي | البرنامج | الفرق |
|---|---|---|---|
| Salaire brut | 47 000.00 | 47 000.00 | 0 |
| Base cotisable | 44 000.00 | 44 000.00 | 0 |
| CNAS salarié | 3 960.00 | 3 960.00 | 0 |
| CNAS patronal | 11 440.00 | 11 440.00 | 0 |
| Base imposable | 40 040.00 | 40 040.00 | 0 |
| IRG brut | 4 610.80 | 4 610.80 | 0 |
| Abattement | 1 500.00 | 1 500.00 | 0 |
| IRG | 3 110.80 | 3 110.80 | 0 |
| **Net** | **34 929.20** | **34 929.20** | **0** |

## الحالة ج — نطاق التنعيم Lissage de tranche (base = 36 000)
- Brut = 36 000 ; Base cotisable = 36 000 → CNAS salarié = 36 000×9 % = **3 240**
- Base imposable = 36 000 − 3 240 = 32 760 ∈ ]30 000 ; 35 000] ⇒ **lissage applicable**
- IRG brut : 20 000×23 % → non, car 32 760 : 20 000×23 %? non — 0–20 000:0 ; 20 000–32 760 : 12 760×23 % = **2 934,80**
- Abattement = 40 %×2 934,80 = 1 173,92 (entre 1000 et 1500 → pas de plafond)
- Après abattement = 2 934,80 − 1 173,92 = 1 760,88
- Lissage : 1 760,88 × 137/51 − 27925/8 = 4 730,2071 − 3 490,625 = **1 239,58**
- Net = 36 000 − 3 240 − 1 239,58 = **31 520,42**

| الخطوة | يدوي | البرنامج | الفرق |
|---|---|---|---|
| Salaire brut | 36 000.00 | 36 000.00 | 0 |
| Base cotisable | 36 000.00 | 36 000.00 | 0 |
| CNAS salarié | 3 240.00 | 3 240.00 | 0 |
| CNAS patronal | 9 360.00 | 9 360.00 | 0 |
| Base imposable | 32 760.00 | 32 760.00 | 0 |
| IRG brut | 2 934.80 | 2 934.80 | 0 |
| Abattement | 1 173.92 | 1 173.92 | 0 |
| IRG (lissé) | 1 239.58 | 1 239.58 | 0 |
| **Net** | **31 520.42** | **31 520.42** | **0** |

## الخلاصة
الثلاث حالات **متطابقة إلى غاية السنتيم** بين الحساب اليدوي المستقلّ وناتج المحرّك — **صفر فرق** في كل خطوة، بما فيها الحالة الأصعب (lissage). لا اكتشاف.
