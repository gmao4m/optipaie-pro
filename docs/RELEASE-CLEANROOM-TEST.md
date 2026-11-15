# Test d'installation sur machine PROPRE — OBLIGATOIRE avant chaque diffusion

> **Pourquoi (dépendances).** La 1.29.0 a planté au démarrage chez des clients :
> « Impossible de charger l'assembly **Newtonsoft.Json, Version=13.0.0.0** ». La DLL était
> pourtant **présente dans le paquet**. Le test de la 1.29.0 avait été fait sur la **machine de
> développement**, qui résout les dépendances autrement (outils/rails installés) — ce qui a
> **masqué** le trou. Un logiciel ne se teste jamais sur la machine qui l'a construit.

> **Pourquoi (rendu des écrans).** La 1.38.0 a planté chez **tous** les clients avec la boîte
> « ...ولم يُغلق البرنامج / l'application reste ouverte » et un premier écran cassé. La cause : le
> tableau de bord héberge un `UserControl` (`WorkforceSection`) qui référençait un style
> `{StaticResource KpiValue}` **local à une autre vue** — invisible depuis le contrôle hébergé.
> Une clé `{StaticResource}` introuvable **compile sans erreur** et **passe les 1600+ tests
> headless** : elle n'explose qu'au **rendu** de l'écran, chez l'utilisateur. L'app **démarrait**
> normalement — le crash n'apparaissait qu'en **ouvrant** l'écran. Conclusion : lancer l'app ne
> suffit pas ; il faut **ouvrir chaque écran** pour prouver qu'il se rend. *(Un test de rendu
> automatique — `OptiPaie.UiTests` — instancie désormais chaque fenêtre/UserControl et échoue sur
> une clé de ressource introuvable ; il tourne en CI. Il complète, mais ne remplace pas, la passe
> manuelle « ouvrir chaque écran » sur machine propre : lui ne voit pas les écrans dont le
> constructeur exige des services runtime, ceux-là ne se prouvent qu'en cliquant.)*

Ce test est une **étape fixe du processus de diffusion**, au même titre que la passe d'isolation
([[RELEASE-ISOLATION-CHECK]]). Aucune release n'est diffusée (`gh release`, bascule de
`version.json`) tant qu'il n'est pas **vert**.

---

## 1. Garde-fous automatiques au build (première ligne — déjà en place)

`installer/build-installer.ps1` refuse de produire un paquet incomplet :

- **Clôture des dépendances managées** : il parcourt la clôture de références **réelle** de
  `OptiPaie PRO.exe` (dérivée des binaires, **jamais une liste manuelle qui se périme**) et
  échoue si une assembly non‑framework manque du paquet (ex. `Newtonsoft.Json.dll`).
- **Interops natives** : il vérifie explicitement `SQLite.Interop.dll` (base de données),
  `libSkiaSharp.dll` / `libHarfBuzzSharp.dll` (fiches de paie QuestPDF) — car une native n'a
  aucune référence managée, donc la clôture ne peut pas la voir.
- Le payload est produit par `dotnet build -o` (pas `dotnet publish` : pour ce projet .NET
  Framework, `publish` **supprime** `SQLite.Interop.dll` ; le garde‑fou natif l'attraperait).

Ces garde‑fous empêchent de **construire** un paquet cassé. Ils ne remplacent pas le test réel
d'installation ci‑dessous.

## 2. Test réel sur machine PROPRE (obligatoire)

**Machine cible :** une **VM Windows neuve** — Windows 10 **et** Windows 7 SP1 idéalement —
**sans Visual Studio, sans .NET SDK, sans aucun de nos outils**, jamais utilisée pour compiler.
Snapshot « propre » à restaurer entre les essais.

> **ON TESTE L'ARTEFACT DU PIPELINE, PAS LE BUILD DÉVELOPPEUR.** Chaque scénario installe le fichier
> **téléchargé depuis la Release GitHub** (`OptiPaie-PRO-Setup.exe` / `OptiPaie-PRO-Update.exe`
> produits par la CI), **jamais** le `.exe` compilé sur la machine de dev. Deux builds du même code
> ne sont pas identiques (horodatage, ordre, environnement) et **le client reçoit le build du
> pipeline** — c'est lui qu'il faut valider. Télécharger : page de la Release → la pièce jointe.

Exécuter **tous** les scénarios (celui qui a cassé est la mise à jour) :

| # | Scénario | Étapes | Résultat attendu |
|---|----------|--------|------------------|
| A | **Installation fraîche** | Installer `OptiPaie-PRO-Setup.exe` (téléchargé de la Release) sur une VM vierge, puis **lancer** l'app | La fenêtre de connexion / tableau de bord s'ouvre. **Aucune** boîte « Erreur d'initialisation ». |
| B | **Mise à jour par‑dessus la version précédente + ouvrir CHAQUE écran** | Installer d'abord la **version publiée actuelle** (celle des clients), la lancer, puis installer la nouvelle `OptiPaie-PRO-Setup.exe` (de la Release) **par‑dessus**, **relancer**, et **ouvrir un par un tous les écrans de premier niveau** | L'app démarre ; toutes les DLL (dont `Newtonsoft.Json.dll`, `SQLite.Interop.dll`) sont présentes ; **chaque écran se rend sans la boîte « ...reste ouverte / ولم يُغلق البرنامج »** ni écran blanc/cassé. |
| C | **Mise à jour d'une base PRÉ‑PERMISSIONS avec de vraies données** (le chemin du propriétaire) | Sur une VM portant la **version publiée actuelle** avec une base **réelle et peuplée** (société, employés, contrats, bulletins), installer la nouvelle (de la Release) **par‑dessus** et **relancer**. **Puis exécuter les 3 contrôles bloquants ci‑dessous.** | Voir **C.1 / C.2 / C.3**. Dans **tous** les cas : **aucune donnée perdue ni modifiée**, et toutes les entrées du menu accessibles au propriétaire. En cas d'échec, message bilingue + **base inchangée**, jamais une vue vide/restreinte. |
| D | **Mise à jour LÉGÈRE d'une install existante** (ce que l'updater sert) | Sur une VM portant la **version publiée actuelle** (donc **avec** .NET 4.8 + VC++ déjà présents), installer `OptiPaie-PRO-Update.exe` (le paquet léger de la Release) **par‑dessus**, puis **relancer** | L'installation est **rapide** et **ne télécharge AUCUN framework** (.NET/VC++ détectés présents → ignorés). L'app démarre, données intactes. **STOP si** l'installation tente de télécharger un framework de ~116 Mo, ou se bloque/échoue à moitié. |

**Contrôles BLOQUANTS du scénario C** (la plupart des 300 clients prennent le chemin « compte adopté ») :
- **C.0 — recensement des données AVANT / APRÈS (aucune perte).** AVANT la mise à jour, sur la
  version publiée actuelle, **noter trois chiffres** : nombre d'employés, nombre de sociétés, et
  **le dernier bulletin de paie** (employé + mois + net). APRÈS la mise à jour, **rouvrir et
  confirmer que les trois sont identiques** (mêmes compteurs, même dernier bulletin au centime).
  **STOP si** l'un des trois a changé — c'est une perte/altération de données, jamais tolérée.
- **C.1 — établissement du propriétaire.** Au premier lancement : soit le compte existant est
  **adopté en propriétaire sans aucun écran** et se connecte avec **son mot de passe actuel** ; soit
  (base sans compte) l'assistant **« Création du compte propriétaire »** bilingue s'affiche, impose un
  mot de passe ≥ 10 et montre le **code de récupération en grand** avec case à cocher.
- **C.2 — code de récupération au premier login (chemin « compte adopté »).** Après s'être connecté
  avec le **compte existant**, le **code de récupération doit être proposé et affiché** (il n'est pas
  montré à l'adoption — il est émis au **premier login réussi**). **Ce que voit le testeur :** un écran
  présentant un code à conserver. **STOP si** aucun code n'apparaît — sans lui, un propriétaire n'a
  **aucune** voie de récupération et personne ne le découvre avant un mot de passe oublié.
- **C.3 — récupération réelle avec ce code.** Se déconnecter, choisir « récupération » depuis l'écran
  de connexion, saisir **le code affiché en C.2** + un nouveau mot de passe, valider. **Ce que voit le
  testeur :** le mot de passe est réinitialisé, l'app ouvre, et un **nouveau** code de récupération est
  affiché (l'ancien est consommé). **STOP si** le code est refusé, ou si aucun nouveau code n'est émis.

**Vérifications à chaque scénario :**
- L'application **atteint son écran** (connexion / activation / tableau de bord) — pas une boîte de dialogue d'erreur.
- Ouvrir le dossier d'installation (clic droit sur le raccourci → « Ouvrir l'emplacement du fichier ») et confirmer la présence de `OptiPaie PRO.exe`, `Newtonsoft.Json.dll`, `System.Data.SQLite.dll`, `SQLite.Interop.dll`, `libSkiaSharp.dll`.
- Faire une action qui touche la base (ouvrir un employé) et une fiche de paie (PDF) — prouve SQLite + Skia chargés.
- **Scénarios B / C / D — vérifier la version remontée dans la CONSOLE ADMIN (bloquant).** Après la
  mise à jour, une fois l'app lancée et connectée au backend, ouvrir la **console d'administration des
  licences** et retrouver cet appareil : il doit afficher **v1.41.0** (le `VersionDisplay` du device),
  pas l'ancienne 1.8.0.0. C'est la **seule** preuve de bout en bout du correctif du piège de version
  (`FileVersion`/`AssemblyVersion` figés à 1.8.0.0) : l'analyse statique a confirmé que `app_version`
  est un champ texte libre que le device remonte et que l'admin **affiche** sans contrainte ni
  comparaison, mais la boucle réelle *device → backend → admin* ne se prouve qu'ici, car la production
  (clients payants) ne se teste pas depuis la machine de build. **STOP si** la console affiche encore
  1.8.0.0 après mise à jour — le correctif de version n'a pas pris.
- **Scénario B — ouvrir CHAQUE écran de premier niveau (bloquant).** Parcourir **toutes** les entrées du menu latéral (Tableau de bord, Employés, Paie, Présence, Congés, Prêts, Contrats, Performance, Actifs, Formation, Recrutement, Déclarations, Paramètres, …) et ouvrir les fenêtres secondaires principales (fiche employé, édition société/employé, profil 360°). Chaque écran doit **s'afficher** — aucune boîte « reste ouverte / ولم يُغلق البرنامج », aucun écran blanc. C'est **exactement** la classe de panne 1.38.0 : une clé `{StaticResource}` introuvable ne se voit qu'au rendu de l'écran concerné. Lancer l'app **ne prouve rien** ; seul l'affichage de chaque écran le prouve.

## 3. Pré‑contrôle local rapide (proxy — ne remplace PAS la VM)

Sur la machine de build, on peut détecter la classe de bug en **isolant** le payload :

1. Copier tout le contenu du dossier de build (`%TEMP%\optipaie_publish`) dans un dossier neuf isolé.
2. Lancer `OptiPaie PRO.exe` depuis ce dossier isolé → il doit ouvrir sa **fenêtre WPF**.
3. **Contrôle négatif** : retirer `Newtonsoft.Json.dll` de la copie et relancer → il doit apparaître une **boîte d'erreur** (classe de fenêtre `#32770`). Si le contrôle négatif ne casse pas, c'est que la machine masque la dépendance (GAC/outils) — le test n'est pas fiable, passer à la VM.

*(Validé le 2026‑08‑20 pour la 1.29.1 : payload complet → fenêtre applicative ; sans Newtonsoft → boîte d'erreur `#32770`, crash reproduit.)*

## 4. Feu vert

Diffusion autorisée uniquement si **tous** ces contrôles sont **verts** :

- garde‑fous de build **verts** ;
- test de rendu automatique `OptiPaie.UiTests` **vert** en CI (aucune clé `{StaticResource}` introuvable) ;
- **scénario A** sur VM propre **vert** (l'app démarre) ;
- **scénario B** sur VM propre **vert** — non seulement l'app démarre par‑dessus la version publiée,
  mais **chaque écran de premier niveau a été ouvert et s'est rendu** sans la boîte « reste
  ouverte / ولم يُغلق البرنامج » (gate **bloquant**) ;
- **scénario C** sur VM propre **vert** (gate **bloquant**) — une base **pré‑permissions peuplée**
  a été mise à jour : **C.0 le recensement AVANT/APRÈS (employés, sociétés, dernier bulletin) est
  identique**, le propriétaire est établi (adopté ou créé), **aucune donnée perdue**, tous
  les écrans accessibles, **ET C.2 le code de récupération est proposé/affiché au premier login du
  compte adopté, ET C.3 ce code réinitialise le mot de passe et un nouveau code est émis**. Doublé
  en automatique par [`PrePermissionUpgradeTests`](../tests/OptiPaie.Tests/PrePermissionUpgradeTests.cs)
  (adoption + backfill du code + consommation/réémission), qui doit être **vert** en CI ; voir
  [[multiuser/07-upgrade]] ;
- **scénario D** sur VM propre **vert** (gate **bloquant**) — le paquet **léger**
  `OptiPaie-PRO-Update.exe` met à jour une install existante **sans télécharger de framework** et
  l'app démarre, données intactes ;
- **console admin** sur les scénarios B/C/D **verte** (gate **bloquant**) — l'appareil mis à jour
  **remonte v1.41.0** dans la console d'administration des licences (plus 1.8.0.0) ;
- [[RELEASE-ISOLATION-CHECK]] **vert**.

Consigner « test machine propre (A fraîche + B mise à jour + **chaque écran ouvert** + **version
v1.41.0 vue dans la console admin** + C propriétaire & récupération & **recensement données
identique** + D paquet léger) : OK » dans les notes de release **avant** `gh release create`.
