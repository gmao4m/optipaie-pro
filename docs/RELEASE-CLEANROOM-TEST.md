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

Exécuter **les deux** scénarios (celui qui a cassé est la mise à jour) :

| # | Scénario | Étapes | Résultat attendu |
|---|----------|--------|------------------|
| A | **Installation fraîche** | Installer `OptiPaie-PRO-Setup.exe` de la nouvelle version sur une VM vierge, puis **lancer** l'app | La fenêtre de connexion / tableau de bord s'ouvre. **Aucune** boîte « Erreur d'initialisation ». |
| B | **Mise à jour par‑dessus la version précédente + ouvrir CHAQUE écran** | Installer d'abord la **version publiée actuelle** (celle des clients), la lancer, puis installer la nouvelle **par‑dessus**, **relancer**, et **ouvrir un par un tous les écrans de premier niveau** (chaque entrée du menu latéral + les fenêtres qu'elles ouvrent) | L'app démarre ; toutes les DLL (dont `Newtonsoft.Json.dll`, `SQLite.Interop.dll`) sont présentes ; **chaque écran se rend sans la boîte « ...reste ouverte / ولم يُغلق البرنامج »** ni écran blanc/cassé. |

**Vérifications à chaque scénario :**
- L'application **atteint son écran** (connexion / activation / tableau de bord) — pas une boîte de dialogue d'erreur.
- Ouvrir le dossier d'installation (clic droit sur le raccourci → « Ouvrir l'emplacement du fichier ») et confirmer la présence de `OptiPaie PRO.exe`, `Newtonsoft.Json.dll`, `System.Data.SQLite.dll`, `SQLite.Interop.dll`, `libSkiaSharp.dll`.
- Faire une action qui touche la base (ouvrir un employé) et une fiche de paie (PDF) — prouve SQLite + Skia chargés.
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
- [[RELEASE-ISOLATION-CHECK]] **vert**.

Consigner « test machine propre (fraîche + mise à jour + **chaque écran ouvert**) : OK » dans les
notes de release **avant** `gh release create`.
