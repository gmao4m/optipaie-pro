# CNAS DAS — État des lieux des lacunes de données

*Étape 2/11 du module Déclarations CNAS. Rapport en lecture seule : aucun code écrit.*
*Objectif : lister les lacunes que la DAS annuelle exige, classer chacune en **migration requise** vs **non**, et isoler ce que je ne peux pas trancher sans dépôt réel. Tu tranches ; ensuite je code.*

Méthode : reconnaissance du schéma (`src/OptiPaie.Data/Sql/Migrations/*.sql`) et des entités, **croisée avec l'analyse des 2 fichiers DAS réels de 2021** (entête + détail, employeur `1639259938`).

---

## Verdict d'ensemble

> **Le module DAS entier peut être livré avec ZÉRO migration de schéma.**
> Chaque champ exigé par la DAS est soit une colonne **déjà existante**, soit une **constante paramétrable par société** stockée via `SettingsService` (le même mécanisme que la cadence — pas une migration). Les manques de *validation* (NSS, date de naissance) sont traités par le **contrôleur bloquant à l'export** (liste nominative) et l'écran de préparation déjà livré (tranche 1), **jamais** par une contrainte `NOT NULL` rétroactive qui casserait des données existantes.

Une seule chose te revient à trancher (voir §7) : veux-tu une **validation bloquante à la saisie** de la fiche salarié, ou seulement à l'export DAS ? Ma recommandation : **seulement à l'export** (non destructif, zéro migration).

---

## 1. Unité de durée + temps partiel

**Ce que montrent les fichiers réels :** l'unité est **`H` (heures)**, pas `M` (mois). Les durées sont des volumes horaires par trimestre : `520` pour un trimestre plein (= 173,33 h/mois × 3), `58`, `433`, `318`, `202`… pour des trimestres partiels. → **Je m'aligne sur les fichiers réels : unité `H`.** L'hypothèse « unité M » de la spec initiale est **écartée** et isolée en constante « à confirmer ».

**Ce que le code fournit :**
- `Payslip.WorkedHours` existe (`Payslip.cs:56`, colonne `0001_InitialSchema.sql:176`, `NOT NULL DEFAULT '0'`).
- **MAIS** il vaut `request.WorkedHours` (`PayrollService.cs:115`), alimenté par l'assiduité horaire ; il **reste à 0** quand la paie tourne sans pointage horaire (cas courant). → `Σ WorkedHours` **n'est pas** une source d'heures fiable à lui seul.
- `StandardHours` existe côté assiduité (`AttendanceService.cs:372,383`) et vaut les heures mensuelles standard (≈ 173,33) → reproduit `520` pour un trimestre plein.
- Aucun indicateur temps-partiel / quotité (`ContractType` n'a pas de valeur « temps partiel »). **Ce n'est pas nécessaire** : le temps partiel est implicite dans le volume d'heures déclaré (moins d'heures = moins de temps).

**Migration ?** **NON.** Règle de calcul des heures (à figer à l'étape 6, contre les fichiers) : par trimestre, `WorkedHours` s'il est > 0, sinon repli sur `StandardHours × nombre de mois avec bulletin`. La règle de repli est **isolée en constante « à confirmer »**.

---

## 2. Plusieurs entrées / sorties pour un même salarié

**Ce que montrent les fichiers :** la ligne détail a **une** date d'entrée (158‑165) et **une** date de sortie (166‑173) par salarié et par an. Les 7 lignes de référence ne portent qu'une entrée et au plus une sortie. → un seul couple entrée/sortie suffit pour la DAS annuelle.

**Ce que le code fournit :**
- `Employee.HireDate` (NOT NULL, validée) + `Employee.ExitDate` (nullable, validée ≥ HireDate) — **mono-épisode**.
- `EmploymentContracts` (`0013_Contracts.sql`) porte **plusieurs** `StartDate`/`EndDate` par salarié (réembauche, renouvellement) — disponible si l'annexe mouvements (étape 3) doit lister plusieurs mouvements dans une période.

**Migration ?** **NON.** La ligne DAS se contente de `HireDate`/`ExitDate`. L'annexe mouvements (étape 3) lira `HireDate`/`ExitDate` de la période, avec `EmploymentContracts` en source plus riche si besoin — sans nouvelle table.

---

## 3. Validation du n° de sécurité sociale

**Ce que montrent les fichiers :** NSS = **12 chiffres, clé incluse dans la même chaîne** (ex. `950075025342`), positions 20‑31. Pas de champ clé séparé.

**Ce que le code fournit :** `Employee.Nss` = colonne texte libre, **aucune contrainte de longueur, aucune validation à la saisie** (`EmployeeValidator` n'a pas de règle NSS). Le service déclarations suppose déjà 12 chiffres à la lecture (`CnasDeclarationService.cs:20,92`).

**Migration ?** **NON.** La clé étant incluse dans la chaîne, **aucune colonne `NssKey` n'est nécessaire**. Le contrôleur bloquant DAS (étape 8) **refuse** tout NSS absent, non numérique ou ≠ 12 chiffres, avec liste nominative. Une colonne clé séparée serait une migration **inutile**.

---

## 4. Date de naissance obligatoire

**Ce que montrent les fichiers :** date de naissance présente sur les 7 lignes (82‑89, `jjmmaaaa`), jamais vide.

**Ce que le code fournit :** `Employee.BirthDate` **nullable**, aucune validation (`Employee.cs:37`, colonne `0001:52`).

**Migration ?** **NON** — et il ne FAUT pas passer la colonne en `NOT NULL` (échouerait sur les fiches existantes à date nulle). Le contrôleur bloquant DAS **refuse** l'export si une date de naissance manque, avec liste nominative. La tranche 1 la signale déjà en préparation.

---

## 5. Centre payeur (entête, positions 15‑19)

**Ce que montrent les fichiers :** l'entête porte un **centre payeur renseigné = `16000`** (5 chiffres). *(Correction de mon hypothèse initiale « zone entête vide » : seule la zone dénomination 20‑128 est vide ; le centre payeur, lui, est rempli.)*

**Ce que le code fournit :** **aucun** champ centre payeur sur `Company`.

**Migration ?** **NON, si** on le stocke en **constante paramétrable par société via `SettingsService`** (clé `Cnas.CentrePayeur.{companyId}`), exactement comme la cadence. Une colonne `Company` serait une migration — évitable. → **je pars sur SettingsService.**

---

## 6. Étanchéité inter-sociétés (rappel, pas une lacune de données)

`Payslip` n'a **pas** de `CompanyId` ; la société n'est atteinte que par `RunId → PayrollRun.CompanyId`. Déjà atténué dans `CnasDeclarationService` (re-vérification `run.CompanyId == companyId`). L'agrégation DAS **réutilisera la même garde**. Aucune migration.

---

## 7. Récapitulatif — migration requise ?

| Champ / besoin DAS | Source retenue | Migration ? |
|---|---|---|
| Assiette / salaire par trimestre | Σ `Payslip.BaseCotisable` par trimestre | **Non** |
| Durée (heures, unité `H`) | `WorkedHours` sinon repli `StandardHours × mois` | **Non** |
| Temps partiel | implicite dans les heures | **Non** |
| Entrée / sortie | `HireDate` / `ExitDate` (mono-épisode suffit) | **Non** |
| NSS 12 ch. + clé | `Employee.Nss` + refus à l'export si invalide | **Non** |
| Date de naissance | `Employee.BirthDate` + refus à l'export si absente | **Non** |
| N° employeur 10 ch. | `Company.CnasEmployerNumber` + refus si invalide | **Non** |
| Centre payeur | `SettingsService` `Cnas.CentrePayeur.{companyId}` | **Non** |

**Total migrations : 0.**

---

## 8. Ce que je ne peux pas trancher sans dépôt réel (isolé en constantes « à confirmer »)

- **Unité `H` vs `M`** : les fichiers 2021 disent `H`. Je m'aligne sur `H`, mais l'unité reste une constante paramétrable (si un centre exige `M` un jour, on change la constante, pas le code).
- **Règle de repli des heures** (`StandardHours × mois` quand `WorkedHours = 0`) : reproduit `520` pour un trimestre plein dans les fichiers de référence, mais la formule exacte d'un trimestre partiel n'est vérifiable que sur un dépôt réel accepté.
- **Indicateur de fin de ligne `0`** (position 194) et **espace anomalie** (entête position 193) : reproduits à l'identique, rôle inconnu.
- **Frontières internes de la zone dénomination** (20‑128) : vides dans les fichiers → 109 espaces, seule configuration prouvée.

---

## 9. La seule décision qui te revient

**Validation à la saisie de la fiche salarié — bloquante ou pas ?**

- **Option A (recommandée)** — validation **uniquement à l'export DAS** (contrôleur bloquant + liste nominative) et signalement doux sur l'écran de préparation. **Non destructif, zéro migration**, n'empêche jamais d'enregistrer une fiche.
- **Option B** — validation **bloquante à la saisie** (NSS 12 chiffres, date de naissance obligatoire dans `EmployeeValidator`). Plus strict, mais **rejette potentiellement des fiches existantes** à la prochaine édition, et durcit un formulaire hors périmètre CNAS.

Je recommande **A**. Dis-moi si tu préfères B (ou un mélange : blocage export + avertissement non bloquant à la saisie).
