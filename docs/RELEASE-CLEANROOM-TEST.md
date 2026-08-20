# Test d'installation sur machine PROPRE — OBLIGATOIRE avant chaque diffusion

> **Pourquoi.** La 1.29.0 a planté au démarrage chez des clients :
> « Impossible de charger l'assembly **Newtonsoft.Json, Version=13.0.0.0** ». La DLL était
> pourtant **présente dans le paquet**. Le test de la 1.29.0 avait été fait sur la **machine de
> développement**, qui résout les dépendances autrement (outils/rails installés) — ce qui a
> **masqué** le trou. Un logiciel ne se teste jamais sur la machine qui l'a construit.

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
| B | **Mise à jour par‑dessus la version précédente** | Installer d'abord la **version publiée actuelle** (celle des clients), la lancer, puis installer la nouvelle **par‑dessus** et **relancer** | L'app démarre normalement ; toutes les DLL (dont `Newtonsoft.Json.dll`, `SQLite.Interop.dll`) sont présentes dans le dossier d'installation. |

**Vérifications à chaque scénario :**
- L'application **atteint son écran** (connexion / activation / tableau de bord) — pas une boîte de dialogue d'erreur.
- Ouvrir le dossier d'installation (clic droit sur le raccourci → « Ouvrir l'emplacement du fichier ») et confirmer la présence de `OptiPaie PRO.exe`, `Newtonsoft.Json.dll`, `System.Data.SQLite.dll`, `SQLite.Interop.dll`, `libSkiaSharp.dll`.
- Faire une action qui touche la base (ouvrir un employé) et une fiche de paie (PDF) — prouve SQLite + Skia chargés.

## 3. Pré‑contrôle local rapide (proxy — ne remplace PAS la VM)

Sur la machine de build, on peut détecter la classe de bug en **isolant** le payload :

1. Copier tout le contenu du dossier de build (`%TEMP%\optipaie_publish`) dans un dossier neuf isolé.
2. Lancer `OptiPaie PRO.exe` depuis ce dossier isolé → il doit ouvrir sa **fenêtre WPF**.
3. **Contrôle négatif** : retirer `Newtonsoft.Json.dll` de la copie et relancer → il doit apparaître une **boîte d'erreur** (classe de fenêtre `#32770`). Si le contrôle négatif ne casse pas, c'est que la machine masque la dépendance (GAC/outils) — le test n'est pas fiable, passer à la VM.

*(Validé le 2026‑08‑20 pour la 1.29.1 : payload complet → fenêtre applicative ; sans Newtonsoft → boîte d'erreur `#32770`, crash reproduit.)*

## 4. Feu vert

Diffusion autorisée uniquement si : garde‑fous de build **verts**, scénarios **A et B** sur VM
propre **verts** (l'app démarre), et [[RELEASE-ISOLATION-CHECK]] **vert**. Consigner
« test machine propre (fraîche + mise à jour) : OK » dans les notes de release avant `gh release create`.
