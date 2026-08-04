# Module 5 — Évaluations (Performance)

Premium module, module key `performance`. Rebuilt from scratch (migration `0028`) around a
single **fair-scoring engine**: absolute simplicity + an objective, evidence-based evaluation.

## 1. Concept

- **Templates per department** — a reusable grid of **criteria**. Each criterion has a
  **category** (comportemental / technique / administratif / **KPI**) and a **scoring type**
  (étoiles /5 · note /20 · pourcentage %).
- **Two weighting modes** (a toggle): *Critères égaux* (simple average) or *Pondéré* (each
  criterion carries a weight %, summing to 100).
- **KPI criterion** = a numeric objective (cible) vs the value realised → an achievement %.
- **Behaviour log** — 👍 / 👎 facts logged as they happen, shown next to the evaluation screen
  so scoring rests on reality, not memory.
- **Periods** — weekly / monthly / yearly; the formal evaluation happens per period.

## 2. Fair score (single source of truth: `OptiPaie.Services/PerformanceService.cs`)

Every criterion is **normalised to 0-100** whatever its type (stars×20, /20×5, % as-is, KPI =
achievement % capped 0-100). Then:

- **Simple** : total = average of the criteria.
- **Pondéré** : total = Σ(score × poids) / Σ(poids).

The 0-100 total maps to a **band**: Excellent ≥ 90 · Très bien ≥ 75 · Bien ≥ 60 · Moyen ≥ 45 ·
Faible. `ComputeLineScore` / `ComputeTotal` / `Classify` are pure/public — the editor shows the
score live as you rate.

## 3. Screens (`Views/PerformanceView.xaml`, 3 tabs)

| Tab | What |
|---|---|
| **Évaluations** | period selector · board (one row per employee: score, band, statut, *Évaluer/Ouvrir*) · *meilleur employé* banner · quick *Enregistrer un comportement* |
| Evaluation screen | employee header + live total/band · behaviour log side panel (with 👍/👎 add) · one card per criterion (slider for ratings, cible/réalisé for KPI) · *Enregistrer* / *Finaliser* |
| **Modèles** | per-department templates — new / edit / duplicate / *par défaut* / delete |
| **Rapports** | Général (moyenne, classement, à accompagner, tendance, meilleur) · Département · Employé (score, forces/faiblesses, tendance, recommandation) — export **PDF / Excel** |

## 4. Smart touches

- *Meilleur employé* of the latest period.
- Decline alert (`IsDeclining`) when a score drops over ≥ 3 consecutive periods.
- A recommendation per employee (promotion / formation / suivi) — a *suggestion only*, never
  edits contracts or payroll.

## 5. Data model — migration `src/OptiPaie.Data/Sql/Migrations/0028_PerformanceRebuild.sql`

`EvalTemplates` · `EvalCriteria` · `EvalPeriods` · `Evaluations` · `EvaluationScores` ·
`BehaviorLogs`. All reference the shared `Employees`/`Companies` tables by id; decimals/dates as
invariant TEXT; soft-delete via `IsDeleted`. One built-in fallback template seeded.

## 6. Cross-module

- 360° profile shows the employee's recent **évaluations** + recent **comportement** (via
  `GetByEmployee` / `GetBehaviors`).
- Notifications surface pending evaluations whose period is closing (`GetReminders`).
- Payroll, attendance, CNAS and contracts are untouched — a pure read of employee/department data.

## 7. Tests

`tests/OptiPaie.Tests/PerformanceServiceTests.cs` — 23 integration cases against a real SQLite
file (scoring per type, simple vs weighted, KPI achievement, the five bands, templates + weight
validation, periods, evaluation lifecycle, behaviour log, the three reports, reminders). Full
suite **1467/1467 passing**; `OptiPaie.Desktop` builds 0 errors.
