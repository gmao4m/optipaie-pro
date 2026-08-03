# Audit en lecture seule — Préparation du module Déclarations CNAS (DAC + DAS)

> Session **lecture seule**. Aucun fichier de code modifié, aucun code écrit. Ce document est le seul livrable.
> Chaque affirmation sur le code cite un chemin réel. Ce qui n'a pas été vérifié est marqué **« non vérifié »**.
> Application livrée = `OptiPaie.Desktop` / `OptiPaie.Services` / `OptiPaie.Core` / `OptiPaie.Data`. `OptiPaie.App` (WinForms) et `OptiPaie.Reporting` (DevExpress) sont **hérités, non livrés**.

---

## A. Réponse directe : l'assiette cotisable est-elle persistée ? — **OUI**

L'assiette cotisable (base soumise à cotisation CNAS) est **figée et persistée par bulletin**, pas seulement recalculée à l'affichage.

**Preuve :**

| Élément | Où | Preuve |
|---|---|---|
| Propriété entité | `Payslip.BaseCotisable` (decimal) | `src/OptiPaie.Core/Entities/Payslip.cs:23` (« Base Cotisable — sum of CNAS-applicable gains ») |
| Colonne en base | `Payslips.BaseCotisable TEXT NOT NULL` | `src/OptiPaie.Data/Sql/Migrations/0001_InitialSchema.sql:165` |
| Écriture (INSERT) | inséré à la génération | `src/OptiPaie.Data/Repositories/PayslipRepository.cs:42-53` |
| Relecture (SELECT *) | remappé par Dapper | `src/OptiPaie.Data/Repositories/PayslipRepository.cs:24-36` (`GetByRun`, `GetByEmployee`, `GetById`) |

Sont **aussi persistés sur le même bulletin** (tout ce qu'il faut pour la DAC/DAS) :

- `CnasEmployee` (part salariale 9 %) — `Payslip.cs:26` / colonne `0001_InitialSchema.sql:166`
- `CnasEmployer` (part patronale) — `Payslip.cs:29` / colonne `:167`. **Le commentaire dit littéralement « Stored for future DAS; not part of net »** — le besoin DAS était déjà anticipé. Idem `PayrollTotals.cs:20` et `src/OptiPaie.PayrollEngine/Rules/CnasRule.cs:9` (« employer share is stored for declarations »).
- `CnasEmployeeRateUsed`, `CnasEmployerRateUsed` (taux au moment du calcul, traçabilité légale) — `Payslip.cs:47-50` / colonnes `:173-174`
- `WorkedDays`, `WorkedHours` — `Payslip.cs:53,56` / colonnes `:175-176`
- `SalaireBrut`, `BaseImposable`, `Irg`, `NetSalaire`, `EngineVersion`, `GeneratedAtUtc` — `Payslip.cs:20,32,41,44,59,62`

Le bulletin est rattaché à **une société + une période** par `Payslip.RunId → PayrollRun` (`PayrollRun.CompanyId`, `PeriodYear`, `PeriodMonth` — `src/OptiPaie.Core/Entities/PayrollRun.cs:16,19,22`).

**Conséquence majeure :** la DAC (cumul mensuel/trimestriel) et la DAS (sommes annuelles par salarié et par trimestre) se construisent **entièrement en RELISANT les bulletins persistés** — sans jamais appeler le moteur de paie. La règle de cohérence « Σ 4 trimestres DAS = cumul DAC de l'année » est **garantie par construction** si les deux lisent la même agrégation des mêmes bulletins.

**Seule réserve (formatage, pas donnée) :** les montants sont stockés en `TEXT` (décimales en chaîne, via un type-handler Dapper). La DAS exige des **centimes entiers sans décimale** (montant × 100) — c'est une étape d'encodage, pas un manque de donnée.

---

## B. Inventaire des données — présentes / absentes pour produire DAC et DAS

| Donnée requise | Requis pour | Présent ? | Emplacement (preuve) | Validé ? | Manque / réserve |
|---|---|---|---|---|---|
| Assiette cotisable / mois | DAC + DAS | **Oui, persistée** | `Payslip.BaseCotisable` (`Payslip.cs:23`, col `0001:165`) | s.o. | — |
| CNAS salarié 9 % / mois | DAC + DAS | **Oui, persistée** | `Payslip.CnasEmployee` (`:26`, col `:166`) | s.o. | — |
| CNAS employeur / mois | DAC + DAS | **Oui, persistée** | `Payslip.CnasEmployer` (`:29`, col `:167`) | s.o. | Montant **global unique** ; pas de détail par branche (voir F) |
| Salaire brut / mois | DAS | Oui, persisté | `Payslip.SalaireBrut` (`:20`, col `:164`) | s.o. | — |
| Trimestre T1..T4 | DAS | Dérivable | `PayrollRun.PeriodMonth` (`PayrollRun.cs:22`) → `T=(mois-1)/3+1` | s.o. | Pas de champ « Trimestre » stocké (calcul trivial) |
| Salaire / trimestre | DAS | **Calculable** | somme des bulletins des mois du trimestre | s.o. | Choix de l'assiette à trancher (voir F) |
| Total annuel | DAS | **Calculable** | somme sur 12 mois | s.o. | — |
| NSS + clé | DAC + DAS | Partiel | `Employee.Nss` (chaîne libre, `Employee.cs:31`, col `0001:50`) | **Non** | **Pas de champ clé** ; nullable ; aucune validation (longueur/chiffres/clé) — `EmployeeValidator.cs` n'a aucune règle NSS |
| Date de naissance | DAC + DAS | Oui mais faible | `Employee.BirthDate` (nullable, `:37`, col `:52`) | **Non** | **Peut être null** ; non validée |
| Date d'entrée | DAC/DAS + EMS | **Oui, solide** | `Employee.HireDate` (non-null, `:40`, col `:53 NOT NULL`) | **Oui** (`EmployeeValidator.cs:45-49`) | — |
| Date de sortie | DAC/DAS + EMS | Oui | `Employee.ExitDate` (nullable, `:43`, col `:54`) | Partiel (`≥HireDate`, `EmployeeValidator.cs:51-55`) | Un **seul** spell (voir EMS) |
| N° employeur CNAS (10 chiffres) | Nom des fichiers `D{YY}E/S{n°}.TXT` | Oui | `Company.CnasEmployerNumber` (`Company.cs:39`, col `0001:27`) | **Non** | Nullable ; aucun contrôle 10 chiffres ; la démo contient des tirets (`DemoDataSeeder.cs:171` = `"09-1024578-90"`) |
| Effectif / périodicité DAC (≥10 mensuel / ≤9 trimestriel) | DAC | **Absent** | calculable via `Employees.GetByCompany(id,false).Count` (`EmployeeRepository.cs:24-30`) | s.o. | Aucun effectif figé par période ; aucun seuil 10/9 en code |
| Durée / trimestre (nb journées) | DAS | Oui mais douteux | `Payslip.WorkedDays` (`:53`, col `:175`) | s.o. | = **jours calendaires 28–31** ; entrée/sortie en cours de mois **non proratisée** en pratique (voir D) |
| Unité de paie M / J / H | DAS | **Absent** | — | — | Aucun champ « unité » sur `Employee`/contrat |
| Quotité / temps partiel | DAS | **Absent** | — | — | `ContractType` (`Enums/ContractType.cs:6-22`) n'a pas de valeur temps partiel ; `EmploymentContract.cs:12-50` non plus |
| Mouvements EMS (ENTREE/SORTIE multiples, réembauche, suspension) | Annexe EMS de la DAC | **Absent** | un seul `HireDate` + un seul `ExitDate` | — | Pas d'entité « historique de mouvements » (`grep Movement/Mouvement` dans Core = néant) |
| Fichier texte longueur fixe + centimes | DAS | **Absent** | — | — | Aucun writer largeur fixe ni encodeur centimes dans tout le dépôt (voir C/D) |

**Lecture rapide :** le cœur financier (assiette, cotisations, salaires par période) est **déjà là et fiable**. Les manques sont : (1) **qualité/format des identifiants** (NSS+clé, n° employeur, date de naissance), (2) **encodeur fichier texte DAS**, (3) **sémantique de « durée »** et mouvements multiples (temps partiel / EMS).

---

## C. Frontière du moteur de paie & règle d'isolation proposée

### Le moteur (à ne JAMAIS modifier)

Assemblage `src/OptiPaie.PayrollEngine/` — tout ce qui suit :
`PayrollCalculationEngine.cs`, `Pipeline/` (`IPayrollRule`, `PayrollCalculationContext`, `WorkingLine`), `Rules/` (`CnasRule`, `CotisableBaseRule`, `ElementResolutionRule`, `GrossSalaryRule`, `IrgRule`, `LissageRule`, `NetSalaryRule`, `TaxableBaseRule`), `Legal/` (`AbattementRule`, `IrgCalculator`, `SmoothingRule`, `LegalProfile`, `BuiltInLegalProfileProvider`…), `ElementCalculation/` (stratégies), `Money/MoneyEngine.cs`, `Validation/PayrollContextValidator.cs`, `EngineVersion.cs`.
Plus, côté Core : `src/OptiPaie.Core/Payroll/CacobatphCalculator.cs` (calcul additif CACOBATPH) et les DTO statutaires `src/OptiPaie.Core/Dtos/PayrollTotals.cs`.

Le moteur n'est appelé que par `PayrollService.Preview(...)` (recalcul en mémoire) et `PayrollService.Generate(...)` (**écrit** un run). — `src/OptiPaie.Services/PayrollService.cs`.

### Point d'entrée LECTURE SEULE pour la DAC/DAS

**`IArchiveService`** — `src/OptiPaie.Core/Interfaces/Services/IArchiveService.cs:11` dont l'en-tête dit : *« Read and reprint operations over archived payroll. **Contains no calculation logic** — it only retrieves and stores already-produced payroll data. »* Méthodes utiles :

- `SearchRuns(long? companyId, int? year, int? month)` (`:14`) → les runs d'une société pour une année ;
- `GetRun(long runId)` (`:17`) → le run avec ses `Payslips` chargés ;
- `GetPayslip(...)` / `GetPayslipsByEmployee(...)` (`:20,23`).

Une DAC/DAS lit ainsi `BaseCotisable`, `CnasEmployee`, `CnasEmployer`, `WorkedDays` **déjà figés**, sans jamais toucher au moteur.

### Règle d'isolation proposée (pas de code — principe)

1. **Lecture seule du calcul** : la DAC/DAS ne lit QUE via `IArchiveService` / `PayslipRepository`. **Interdits** : `PayrollService.Preview` (recalcule ⇒ recouple au moteur + valeurs non figées) et `PayrollService.Generate` (écrit).
2. **Ne jamais recalculer une cotisation** : lire `Payslip.CnasEmployee` / `CnasEmployer` (le moteur les a déjà posés) plutôt que réappliquer un taux — sinon risque de dérive vs le bulletin légal.
3. **Société obligatoire** : toujours passer un `companyId` concret (non null, non 0) issu de `CompanyContext.Active`. Le lecteur exemplaire à copier est `ArchiveViewModel` (`src/OptiPaie.Desktop/ViewModels/ArchiveViewModel.cs:91-107` : société active gardée, `SearchRuns(SelectedCompany.Id,…)`, parcours run→bulletins dans ce seul périmètre).

---

## D. Risques de casse — classés par gravité

### 🔴 Élevé

1. **Fuite inter-sociétés.** `IArchiveService.SearchRuns` prend un `companyId` **nullable** ; `null` = **toutes les sociétés** (filtre appliqué seulement si `HasValue` — `src/OptiPaie.Data/Repositories/PayrollRunRepository.cs:34-41`). `Payslip` n'a **aucun** `CompanyId` (portée uniquement par `RunId→PayrollRun` / `EmployeeId→Employee` — `Payslip.cs:11-17`) : aucune défense en profondeur. Précédent réel de balayage tous-sociétés : `SearchRuns(null,null,null)` dans le hérité (`src/OptiPaie.App/Modules/Dashboard/DashboardControl.cs:266`). Une DAS mal scopée mélangerait les salaires d'autres sociétés dans une déclaration légale — **erreur à une ligne, sans garde compilateur**. `GetRun/GetPayslip` ne re-vérifient pas la société (`ArchiveService.cs:32-68`).
2. **NSS invalide/absent.** `Employee.Nss` chaîne libre, nullable, **zéro validation** (`EmployeeValidator.cs:11-58` sans règle NSS ; démo non conforme `DemoDataSeeder.cs:245`). Un export émettrait des NSS manquants/malformés → **rejet CNAS**.
3. **N° employeur malformé.** `Company.CnasEmployerNumber` nullable, non validé, peut contenir des tirets. Le nom de fichier `D{YY}E/S{n°}.TXT` (10 chiffres) serait construit à partir d'une valeur invalide ou vide.
4. **Réutiliser l'écrivain CSV pour le TXT DAS.** Le patron d'export existant (`ReportsViewModel.cs:172-221`) ajoute un **BOM UTF-8**, des séparateurs `;` et passe par `Escape()`. Un fichier DAS largeur fixe exige **aucun BOM, aucun séparateur, largeurs d'octets exactes**. Réutiliser ce helper tel quel **corromprait** le fichier. Seul le squelette `SaveFileDialog + File.WriteAllText` est réutilisable, avec un nouvel encodeur.

### 🟠 Moyen

5. **Entrée/sortie en cours de mois non proratisée.** Le moteur *sait* proratiser (`ElementResolutionRule.cs:34-43`) et `PayrollService` *sait* dériver les jours depuis `HireDate/ExitDate` (`PayrollService.cs:135-169`) **mais uniquement si `WorkableDays==0`**. Or les deux chemins livrés (`PayrollViewModel.cs:368-400`, `BatchPayrollService.cs:174,220-243`) passent toujours `WorkableDays = jours du mois`. Résultat : `Payslip.WorkedDays` d'un entrant/sortant vaut un **mois plein** (sauf absences pointées). Une « durée par trimestre » tirée de là **sur-déclarerait** le trimestre concerné.
6. **Périodicité DAC déduite à chaud.** Aucun effectif figé ; le seuil ≥10/≤9 se déduirait de `GetByCompany(id,false).Count` (actifs non supprimés), ce qui peut mal classer une période (entrées/sorties en cours de période, actifs non déclarables).
7. **`WorkedDays` = jours calendaires (28–31),** pas une convention CNAS (30 fixes ? jours réels ?). Une somme trimestrielle naïve donne ~90–92 au lieu d'une valeur normalisée.
8. **Réutiliser l'agrégation Dashboard/Notifications.** `DashboardService.Build()` et `NotificationService.GetNotifications()` **itèrent `_companies.GetAll()`** (`DashboardService.cs:62`, `NotificationService.cs:50`) : portée **tout le portefeuille**. Copier cette forme dans une DAC/DAS = chiffres multi-sociétés sous le nom d'une seule.
9. **Taux/branches employeur.** L'app stocke un taux patronal **26 %** (`0002_SeedReferenceData.sql:39`, `CNAS_EMPLOYER_RATE 0.26`) en un **montant unique** `CnasEmployer`. Le contexte métier cite « 25 % + 0,5 % œuvres sociales ». Si la DAS attend le **détail par branche**, il n'est pas stocké (voir F). *Non vérifié : ce que la DAS exige exactement.*

### 🟡 Faible

10. **Date de naissance null** possible → lignes DAC/DAS sans date de naissance.
11. **Un seul entrée + une seule sortie** par salarié : réembauche / suspension / changement de salaire en cours d'année **non reconstituables** pour l'annexe EMS.
12. **Collision de noms** : des clés/rapports **« DAS-CACOBATPH » / « DAC-CACOBATPH »** existent déjà (`ReportService.cs:36-37,381,438`) — fonds *différent* (construction). Bien nommer le nouveau module CNAS pour éviter la confusion dev/utilisateur.

---

## E. Plan d'implémentation en tranches verticales (chemin le plus court et le plus sûr)

Principe : **la première tranche est la plus petite possible**, chaque tranche est livrable et testable seule, et **aucune ne touche le moteur**. Cible utilisateur = **comptable pressé** ⇒ priorité à l'usage simple.

### Tranche 1 — « Contrôle de préparation CNAS » (lecture seule, aucun export) — *la plus petite*
Un écran/rapport qui, pour la **société active** et une année choisie, **liste les blocages de données** : salariés sans NSS ou NSS non conforme, date de naissance manquante, et n° employeur CNAS société absent/non conforme (10 chiffres). **Aucune écriture, aucun calcul moteur, aucun fichier produit.**
- Valeur immédiate : le comptable voit ce qui empêchera la déclaration, avant tout le reste.
- Point d'ancrage tests : `tests/OptiPaie.Tests/ReportServiceTests.cs` (fixture SQLite réelle déjà en place, `SetUp` lignes 42-70).
- Dé-risque les tranches suivantes (NSS/n° employeur/DOB) sans rien casser.

### Tranche 2 — DAC d'une période (récapitulatif PDF, société scopée)
Agréger les bulletins **persistés** (via `IArchiveService`, `companyId` obligatoire) de la période → totaux DAC (assiette cotisable, cotisations 9 % / part patronale, effectif) → **PDF** en réutilisant le rendu QuestPDF existant (`ReportDocument.cs:15-88`, patron `FicheService.ExportPdf` `FicheService.cs:109,121-124`). Ajouter l'annexe **EMS** (entrées/sorties dont `HireDate`/`ExitDate` tombent dans la période) en page 2.
- Toujours lecture seule ; réutilise le catalogue de rapports `ReportService` (`Services/ReportService.cs:68-119`).

### Tranche 3 — DAS annuelle : fichiers `D{YY}E.TXT` + `D{YY}S.TXT`
Le morceau nouveau : **encodeur largeur fixe + centimes** (à écrire, aucun équivalent au dépôt), agrégation par salarié et par trimestre depuis les bulletins persistés, n° employeur (normalisé 10 chiffres) dans le nom de fichier. Réutiliser **uniquement** le squelette d'enregistrement fichier (`SaveFileDialog + File.WriteAllText`, cf. `ReportsViewModel.cs:172-205`) avec un encodage dédié (pas de BOM/pas de séparateur — voir D-4).
- **Cohérence garantie** : réutiliser exactement l'agrégation de la Tranche 2 ⇒ Σ trimestres DAS = cumul DAC par construction.

*(Éventuelle Tranche 4, seulement si la « durée » CNAS l'exige : corriger la proratisation entrée/sortie — décision produit, pas une dépendance des tranches 1–3.)*

---

## F. Ce que je n'ai PAS pu déterminer — questions avant toute ligne de code

1. **Spécification technique CNAS non présente au dépôt (non vérifié).** Format largeur fixe exact (offsets/longueurs de chaque champ, remplissage, alignement), **encodage** (Windows-1252 vs UTF-8 sans BOM), **fin de ligne** (CRLF / aucune), et padding des centimes. Rien de tout cela n'existe en code. **Peux-tu fournir le cahier des charges / un exemple de fichier `D{YY}E.TXT` et `D{YY}S.TXT` valides ?**
2. **Part patronale — taux et branches.** L'app stocke **26 %** en un montant unique (`0002_SeedReferenceData.sql:39` ; `Payslip.CnasEmployer`). Le brief dit 25 % + 0,5 % œuvres sociales (total 34,5 %), l'app totalise 35 %. **La DAS attend-elle le détail par branche (retraite/maladie/œuvres…)** — non stocké aujourd'hui — ou le montant patronal global suffit-il ? Le taux 26 % vs 25,5 % est-il à corriger ?
3. **NSS + clé.** Stocker un seul champ 12 caractères avec validateur de clé, ou séparer base(10)+clé(2) ? (aujourd'hui : une seule colonne libre.)
4. **Assiette DAS.** Quelle valeur alimente « salaire par trimestre » : `SalaireBrut`, `BaseCotisable`, ou une assiette CNAS spécifique ? (les trois sont stockées par mois.)
5. **Durée / trimestre.** Convention attendue : `WorkedDays` calendaires (28–31), 30 normalisés, ou jours réellement travaillés ? Et faut-il **activer la proratisation entrée/sortie** dans la paie livrée (laisser `WorkableDays=0`), ou l'absence pointée reste-t-elle le seul mécanisme ?
6. **Effectif pour le seuil DAC (≥10 / ≤9).** Définition retenue : effectif déclarable de la période, ou nombre d'actifs courant (seul disponible) ? Faut-il figer l'effectif par période ?
7. **EMS — mouvements multiples ?** Réembauche / suspension / changement de salaire dans l'année : requis dans l'annexe, ou une seule entrée + une seule sortie suffisent ? (le modèle ne gère qu'un spell.)
8. **Mono-société ou bureau/holding ?** `User` n'a **pas** de `CompanyId` (`src/OptiPaie.Core/Entities/User.cs`) ⇒ l'app est pensée multi-sociétés gérées par un même opérateur. Confirmes-tu que **la société active doit être le périmètre obligatoire et vérifié** de chaque lecture (pas supposé) ?
9. **Identifiants d'en-tête DAS.** Un **code centre/agence CNAS** (souvent en tête de DAS) est-il requis en plus du n° employeur ? Aucun champ de ce type sur `Company`. Le `NationalId` salarié (présent, non validé — `Employee.cs:34`) est-il requis en plus du NSS ?

---

### Note de méthode
Investigation menée en lecture seule : entités, migrations, dépôts, services, `IArchiveService`, validateurs, et suite de tests (`tests/OptiPaie.Tests/`, NUnit 3, SQLite fichier réel, 30 fixtures / ~303 `[Test]`, ancre CNAS = `ReportServiceTests.cs`). La frontière du moteur (section C) a été vérifiée directement dans `src/OptiPaie.PayrollEngine/` et `IArchiveService.cs`.
