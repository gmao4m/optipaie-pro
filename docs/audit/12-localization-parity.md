# 12 — Parité de localisation (FR ⇄ AR) & chaînes en dur

> **ملخص**: تطابق تامّ لمفاتيح الموارد بين الفرنسية والعربية (0 ناقص في كلا الاتجاهين، 0 تكرار).
> 6 قيم متطابقة حرفيًا لكنها كلها مقصودة. ~240 نصّ واجهة مكتوب مباشرة في الكود (خارج نظام الموارد)،
> 114 منها بالفرنسية فقط داخل تطبيق سطح المكتب. لم يُترجم أي شيء — هذا تقرير فقط.

État : **main @ v1.40.1** (`ea46d1c`). Audit en lecture seule, aucune traduction appliquée.
Fichiers comparés : `src/OptiPaie.Localization/Resources/Strings.resx` (FR, neutre) et
`…/Strings.ar.resx` (AR). Clés de type chaîne uniquement (entrées binaires/typées exclues).

**Note préalable (déduplication MSB3568).** Les 5 clés signalées en doublon — `Payroll_Year`,
`Payroll_Elements`, `Payroll_Save`, `Payroll_Employee`, `Payroll_Month` — **n'existent déjà qu'une
seule fois sur `main`** : le bloc « Payroll view labels » dupliqué avait été retiré en amont. La
branche de maintenance 1.40.1 ne porte donc **aucun doublon** (vérifié : 0 clé répétée dans les deux
fichiers, 1498 clés chacun). `Payroll_Save` (AR) vaut bien `حفظ الراتب` — libellé conservé tel quel
sur décision du propriétaire. Il n'y avait donc rien à dédupliquer ; l'apport de ce commit est le
présent audit + le test de non-régression (voir § Prévention).

Méthode § D : scan heuristique des littéraux visibles (XAML `Text/Content/Header/Title/ToolTip`
non `{loc:Loc …}`/`{Binding}` ; C# `MessageBox.Show`, `Dialogs.{Error,Info,Confirm,ErrorWithLog,Prompt}`,
`Title=/Filter=`, `.Text("…")` des documents imprimés). Les appels de **journalisation**
(`Logger.*`, `_logger.*`) sont exclus (non visibles par l'utilisateur). Filtre « texte utilisateur » :
présence d'un caractère accentué, arabe, ou d'une locution à espace — ce qui écarte les énumérations
XAML (`Auto`, `Manual`…) et les clés de ressource.

---

## A) Clés présentes en FR, absentes en AR

Ces clés s'afficheraient en **français dans l'interface arabe**.

**Total : 0.** ✅ Aucune. Parité complète (1498 clés FR, toutes présentes en AR).

## B) Clés présentes en AR, absentes en FR

Ressources mortes ou mal nommées.

**Total : 0.** ✅ Aucune. (1498 clés AR, toutes présentes en FR.)

## C) Clés présentes dans les deux mais à valeur AR **identique** à la valeur FR

Souvent du texte copié non traduit.

**Total : 6** — mais **toutes intentionnelles** (marque, symbole, espace réservé, nom de thème,
acronyme, format). Aucune n'est un oubli de traduction.

| Clé | Valeur (FR = AR) | Évaluation |
|---|---|---|
| `App_Title` | `OptiPaie DZ` | Nom du produit — identique voulu (marque) |
| `Common_None` | `—` | Tiret cadratin — symbole, pas du texte |
| `Perf_BehaviorCount` | `👍 {0}  ·  👎 {1}` | Emojis + espaces réservés — identique voulu |
| `Theme_Office` | `Office` | Nom de thème (couleurs) — nom propre |
| `Cnas_Tab_Dac` | `DAC` | Acronyme CNAS — identique dans les deux langues |
| `Perf_BestFormat` | `{0} — {1}/100 ({2})` | Chaîne de format — pas de mot traduisible |

---

## D) Chaînes visibles écrites directement dans le code (hors système de ressources)

Ces chaînes **ne changent jamais** quand l'utilisateur bascule de langue (sauf celles déjà bilingues).

**Total : 240 littéraux visibles** (scan heuristique — confirmation manuelle recommandée avant action).
Point clé : le **cœur utilisateur** (`OptiPaie.Desktop`) porte **138** de ces littéraux, dont **114
en français seul** (vrai écart) ; les documents imprimés (`OptiPaie.Reporting`) sont, eux, **déjà
localisés** (0 littéral).

Observation utile : les dialogues `Dialogs.ErrorWithLog(…)` sont **bilingues FR+AR dans un même
littéral** (ex. « Impossible de générer le PDF. تعذّر إنشاء ملف PDF. ») — hors ressources mais
affichés dans les deux langues. Les écarts réels sont les `Dialogs.Error/Info/Confirm` **français
seuls** et les libellés XAML en dur.

#### Répartition par projet

| Projet | Nb | Nature |
|---|---:|---|
| OptiPaie.Desktop | 138 | application WPF **utilisateur final** — cible prioritaire |
| OptiPaie.Admin | 80 | outil d'administration **interne** des licences — français uniquement par conception |
| OptiPaie.Services | 18 | titres de rapports / notifications / données de démo |
| OptiPaie.App | 4 | hôte historique (WinForms) — résiduel |
| **Total** | **240** | (OptiPaie.Reporting : **0** — déjà localisé via `_loc`) |

#### Répartition par langue du littéral

| Langue | Nb | Lecture |
|---|---:|---|
| FR/latin seul | 216 | affiché en **français** même en interface arabe — vrai écart |
| bilingue (FR+AR) | 21 | littéral **bilingue** FR+AR — s'affiche dans les deux langues (acceptable) |
| AR seul | 3 | littéral arabe uniquement |

#### Répartition par catégorie (canal)

| Catégorie | Nb |
|---|---:|
| `xaml-label` (Text/Content) | 97 |
| `cs-savedialog/title` (Title=/Filter=, titres de rapport) | 40 |
| `cs-Dialogs.Error` | 21 |
| `cs-Dialogs.Info` | 19 |
| `cs-Dialogs.ErrorWithLog` (bilingue) | 18 |
| `xaml-header/column` (en-têtes de colonnes) | 16 |
| `xaml-window-title` | 11 |
| `cs-Dialogs.Confirm` | 10 |
| `xaml-tooltip` | 5 |
| `cs-header/label` (code) | 3 |

#### Les 50 premiers par visibilité (boîtes de dialogue modales)

Les boîtes modales (bloquantes) sont les plus visibles ; elles occupent à elles seules les 50 premières places.

| # | Fichier : ligne | Canal | Texte | Langue |
|---:|---|---|---|---|
| 1 | `src/OptiPaie.Admin/ViewModels/BulkViewModel.cs:110` | cs-Dialogs.Info | Exporté : | FR seul |
| 2 | `src/OptiPaie.Admin/ViewModels/BulkViewModel.cs:126` | cs-Dialogs.Info | PDF généré : | FR seul |
| 3 | `src/OptiPaie.Admin/ViewModels/BulkViewModel.cs:128` | cs-Dialogs.Error | Génération du PDF impossible : | FR seul |
| 4 | `src/OptiPaie.Admin/ViewModels/DevicesViewModel.cs:41` | cs-Dialogs.Confirm | Libérer cet appareil ? La licence pourra être réactivée ailleurs. | FR seul |
| 5 | `src/OptiPaie.Admin/ViewModels/LicenseEditViewModel.cs:185` | cs-Dialogs.Error | Copie impossible : | FR seul |
| 6 | `src/OptiPaie.Admin/ViewModels/LicenseEditViewModel.cs:241` | cs-Dialogs.Confirm | Réinitialiser tous les appareils de cette licence ? | FR seul |
| 7 | `src/OptiPaie.Admin/ViewModels/LicenseEditViewModel.cs:242` | cs-Dialogs.Info | Appareils réinitialisés. | FR seul |
| 8 | `src/OptiPaie.Admin/ViewModels/LicenseEditViewModel.cs:249` | cs-Dialogs.Confirm | Supprimer définitivement cette licence ? | FR seul |
| 9 | `src/OptiPaie.Admin/ViewModels/LicenseEditViewModel.cs:250` | cs-Dialogs.Info | Licence supprimée. | FR seul |
| 10 | `src/OptiPaie.Admin/ViewModels/UpdatesViewModel.cs:58` | cs-Dialogs.Error | La version est requise. | FR seul |
| 11 | `src/OptiPaie.Admin/ViewModels/UpdatesViewModel.cs:70` | cs-Dialogs.Info | Version publiée. | FR seul |
| 12 | `src/OptiPaie.Admin/ViewModels/UpdatesViewModel.cs:93` | cs-Dialogs.Confirm | Supprimer cette version ? | FR seul |
| 13 | `src/OptiPaie.Desktop/ViewModels/ArchiveViewModel.cs:157` | cs-Dialogs.Error | Ce bulletin est introuvable. | FR seul |
| 14 | `src/OptiPaie.Desktop/ViewModels/ArchiveViewModel.cs:174` | cs-Dialogs.ErrorWithLog | Impossible de produire la fiche de paie. تعذّر إنشاء كشف الراتب. | bilingue |
| 15 | `src/OptiPaie.Desktop/ViewModels/AssetActions.cs:68` | cs-Dialogs.Info | Ce matériel n'est attribué à personne. | FR seul |
| 16 | `src/OptiPaie.Desktop/ViewModels/Attendance/AttendanceMatrixViewModel.cs:551` | cs-Dialogs.Info | Sélectionnez une entreprise. | FR seul |
| 17 | `src/OptiPaie.Desktop/ViewModels/AttendanceMonthViewModel.cs:114` | cs-Dialogs.ErrorWithLog | Impossible de générer le PDF. تعذّر إنشاء ملف PDF. | bilingue |
| 18 | `src/OptiPaie.Desktop/ViewModels/AttendanceMonthViewModel.cs:150` | cs-Dialogs.ErrorWithLog | Impossible de générer le fichier. تعذّر إنشاء الملف. | bilingue |
| 19 | `src/OptiPaie.Desktop/ViewModels/AttendanceMonthViewModel.cs:175` | cs-Dialogs.Info | Fichier enregistré : | FR seul |
| 20 | `src/OptiPaie.Desktop/ViewModels/AttendanceSettingsViewModel.cs:51` | cs-Dialogs.Error | Nombre d'heures standard invalide. | FR seul |
| 21 | `src/OptiPaie.Desktop/ViewModels/AttendanceSettingsViewModel.cs:57` | cs-Dialogs.Error | Tolérance de retard invalide. | FR seul |
| 22 | `src/OptiPaie.Desktop/ViewModels/BatchPayrollViewModel.cs:204` | cs-Dialogs.ErrorWithLog | Le traitement de la paie a échoué. فشلت معالجة الأجور. | bilingue |
| 23 | `src/OptiPaie.Desktop/ViewModels/BatchPayrollViewModel.cs:238` | cs-Dialogs.Error | Ce bulletin est introuvable. | FR seul |
| 24 | `src/OptiPaie.Desktop/ViewModels/BatchPayrollViewModel.cs:245` | cs-Dialogs.ErrorWithLog | Impossible d'ouvrir la fiche. تعذّر فتح الكشف. | bilingue |
| 25 | `src/OptiPaie.Desktop/ViewModels/BatchPayrollViewModel.cs:285` | cs-Dialogs.ErrorWithLog | Impossible d'exporter les bulletins. تعذّر تصدير الكشوف. | bilingue |
| 26 | `src/OptiPaie.Desktop/ViewModels/CertificateEditViewModel.cs:101` | cs-Dialogs.Error | Sélectionnez un employé. | FR seul |
| 27 | `src/OptiPaie.Desktop/ViewModels/CertificateEditViewModel.cs:107` | cs-Dialogs.Error | Sélectionnez un type d'attestation. | FR seul |
| 28 | `src/OptiPaie.Desktop/ViewModels/CertificateViewModel.cs:137` | cs-Dialogs.Info | Sélectionnez d'abord une entreprise. | FR seul |
| 29 | `src/OptiPaie.Desktop/ViewModels/CertificateViewModel.cs:146` | cs-Dialogs.Info | Aucun employé dans cette entreprise. | FR seul |
| 30 | `src/OptiPaie.Desktop/ViewModels/CertificateViewModel.cs:198` | cs-Dialogs.ErrorWithLog | Impossible de générer le PDF. تعذّر إنشاء ملف PDF. | bilingue |
| 31 | `src/OptiPaie.Desktop/ViewModels/CertificateViewModel.cs:204` | cs-Dialogs.Confirm | Supprimer définitivement cette attestation ? | FR seul |
| 32 | `src/OptiPaie.Desktop/ViewModels/CompanyEditViewModel.cs:178` | cs-Dialogs.Error | Ce logo est trop volumineux ( | FR seul |
| 33 | `src/OptiPaie.Desktop/ViewModels/CompanyEditViewModel.cs:190` | cs-Dialogs.Error | Ce fichier n'est pas une image lisible ( | FR seul |
| 34 | `src/OptiPaie.Desktop/ViewModels/ContractViewModel.cs:355` | cs-Dialogs.ErrorWithLog | Impossible de générer le PDF. تعذّر إنشاء ملف PDF. | bilingue |
| 35 | `src/OptiPaie.Desktop/ViewModels/EmployeeEditViewModel.cs:65` | cs-Dialogs.Error | Le nom et le prénom sont obligatoires. | FR seul |
| 36 | `src/OptiPaie.Desktop/ViewModels/EmployeeEditViewModel.cs:71` | cs-Dialogs.Error | L'employé doit être rattaché à une entreprise. | FR seul |
| 37 | `src/OptiPaie.Desktop/ViewModels/EmployeeProfileViewModel.cs:409` | cs-Dialogs.Error | Ce bulletin est introuvable. | FR seul |
| 38 | `src/OptiPaie.Desktop/ViewModels/EmployeeProfileViewModel.cs:416` | cs-Dialogs.ErrorWithLog | Impossible d'ouvrir le bulletin. تعذّر فتح الكشف. | bilingue |
| 39 | `src/OptiPaie.Desktop/ViewModels/EmployeeProfileViewModel.cs:447` | cs-Dialogs.ErrorWithLog | Impossible d'exporter le dossier. تعذّر تصدير الملف. | bilingue |
| 40 | `src/OptiPaie.Desktop/ViewModels/EmployeesViewModel.cs:189` | cs-Dialogs.Error | Créez d'abord une entreprise, puis ajoutez des employés. | FR seul |
| 41 | `src/OptiPaie.Desktop/ViewModels/EmployeesViewModel.cs:236` | cs-Dialogs.Confirm | Voulez-vous vraiment supprimer cet employé ? | FR seul |
| 42 | `src/OptiPaie.Desktop/ViewModels/LeaveBalancesViewModel.cs:100` | cs-Dialogs.ErrorWithLog | Impossible de générer le PDF. تعذّر إنشاء ملف PDF. | bilingue |
| 43 | `src/OptiPaie.Desktop/ViewModels/LeaveBalancesViewModel.cs:128` | cs-Dialogs.ErrorWithLog | Impossible de générer le fichier. تعذّر إنشاء الملف. | bilingue |
| 44 | `src/OptiPaie.Desktop/ViewModels/LeaveBalancesViewModel.cs:152` | cs-Dialogs.Info | Fichier enregistré : | FR seul |
| 45 | `src/OptiPaie.Desktop/ViewModels/LeaveSettingsViewModel.cs:113` | cs-Dialogs.Error | Jours acquis par mois invalide. | FR seul |
| 46 | `src/OptiPaie.Desktop/ViewModels/LeaveSettingsViewModel.cs:119` | cs-Dialogs.Error | Plafond annuel invalide. | FR seul |
| 47 | `src/OptiPaie.Desktop/ViewModels/LeaveSettingsViewModel.cs:125` | cs-Dialogs.Error | Durée du congé de maternité invalide. | FR seul |
| 48 | `src/OptiPaie.Desktop/ViewModels/LeaveTypesViewModel.cs:157` | cs-Dialogs.Error | Durée légale invalide. | FR seul |
| 49 | `src/OptiPaie.Desktop/ViewModels/LeaveTypesViewModel.cs:196` | cs-Dialogs.Confirm | Désactiver ce type ? Il ne sera plus proposé à la saisie. | FR seul |
| 50 | `src/OptiPaie.Desktop/ViewModels/LeaveViewModel.cs:648` | cs-Dialogs.ErrorWithLog | Impossible de générer le PDF. تعذّر إنشاء ملف PDF. | bilingue |

*(190 littéraux supplémentaires au-delà des 50 premiers : en-têtes de colonnes, libellés XAML,
titres de fenêtres, info-bulles — reproductibles avec la méthode § D ci-dessus.)*

---

## Prévention (test de non-régression — dans la suite, pas un script séparé)

`tests/OptiPaie.Tests/LocalizationResxParityTests.cs` (NUnit, s'exécute sous `dotnet test`/CI) :

- **`No_duplicate_keys_within_each_resx_file`** — échoue si une clé est déclarée deux fois dans
  `Strings.resx` ou `Strings.ar.resx` (la cause exacte des avertissements MSB3568).
- **`French_and_Arabic_resx_have_the_same_set_of_keys`** — échoue si une clé est ajoutée au FR sans
  l'ajouter à l'AR (ou l'inverse). Aucune liste d'exceptions n'est nécessaire : la parité est
  **actuellement de 0/0**, donc le test passe au vert et ne se déclenchera que sur une dérive future.

Les deux lisent les `.resx` réels à l'exécution (racine du dépôt découverte via `TestPaths.RepoRoot()`),
donc tout nouveau doublon ou toute nouvelle clé orpheline casse la suite automatiquement.

## Synthèse des quatre compteurs

| Mesure | Valeur |
|---|---:|
| A) Clés FR absentes en AR | **0** |
| B) Clés AR absentes en FR | **0** |
| C) Valeurs FR = AR (non traduites) | **6** (toutes intentionnelles) |
| D) Chaînes visibles en dur dans le code | **240** (dont 114 FR seul dans OptiPaie.Desktop) |

*Aucune traduction appliquée ni publication — audit uniquement.*
