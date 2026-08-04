# Mises à jour automatiques — OptiPaie PRO

À chaque ouverture (puis toutes les 24 h), l'application vérifie **en arrière-plan**
s'il existe une version plus récente. Si oui, une **pop-up** verte/or apparaît :
l'utilisateur clique sur **« Télécharger la mise à jour »**, la barre de progression
avance, puis le programme lance l'installateur et se ferme pour terminer l'installation.
S'il n'y a **pas d'internet**, la vérification échoue en silence et l'application s'ouvre
normalement (elle fonctionne hors-ligne).

## Comment ça marche (technique)

- La version **produit** est lue depuis `AssemblyInformationalVersion`, c.-à-d.
  `<Version>` dans `Directory.Build.props` — **le seul endroit à modifier** à chaque
  sortie. (`AssemblyVersion`/`FileVersion` restent figés à `1.8.0.0` pour la stabilité des
  bindings ; ils ne servent PAS à la comparaison de version.)
- La source de mise à jour par défaut est un simple fichier **`version.json`** hébergé en
  ligne (clé `Update.VersionJsonUrl` dans `App.config`). Format :

  ```json
  {
    "latest_version": "1.15.0",
    "download_url": "https://github.com/gmao4m/optipaie-pro/releases/latest/download/OptiPaie-PRO-Setup.exe",
    "release_notes": "• Nouveautés…",
    "mandatory": false
  }
  ```

- `mandatory: true` masque le bouton « Plus tard » et **force** la mise à jour.
- Les données utilisateur (base SQLite, réglages, licence, modules) vivent dans
  `%AppData%\OptiPaie DZ` : l'installation/mise à jour ne les touche jamais.
- Un canal **GitHub Releases** existe aussi en repli (`Update.GitHubRepo`) ; `version.json`
  a la priorité lorsqu'il est renseigné.

## Hébergement du `version.json` (une seule fois)

Le fichier `version.json` est à la **racine du dépôt**. `App.config` pointe déjà sur sa
version « raw » GitHub :

```
Update.VersionJsonUrl =
  https://raw.githubusercontent.com/gmao4m/optipaie-pro/main/version.json
```

> ℹ️ `raw.githubusercontent.com` est mis en cache par le CDN de GitHub (~5 min) : après
> un `push`, la nouvelle version est vue par les clients au bout de quelques minutes.
> Pour une propagation instantanée, on peut héberger `version.json` ailleurs (GitHub
> Pages, un bucket Supabase Storage public, tout hébergeur statique) et changer l'URL.

## À CHAQUE nouvelle version — 5 étapes

1. **Monter le numéro de version** (un seul endroit) : dans
   `Directory.Build.props`, passer `<Version>1.14.0</Version>` → `1.15.0`.

2. **Construire l'installateur** (Release + Setup.exe WiX) :

   ```bash
   dotnet build src/OptiPaie.Desktop/OptiPaie.Desktop.csproj -c Release
   ```
   puis générer `Setup.exe` via le projet `installer/` (voir `installer/`), en pensant à
   aligner la version dans `installer/Package.wxs` et `installer/Bundle.wxs`
   (`Version="1.15.0.0"`).

3. **Publier une Release GitHub** et **y joindre l'installateur** en tant qu'asset nommé
   exactement **`OptiPaie-PRO-Setup.exe`** :
   - GitHub → *Releases* → *Draft a new release*
   - *Tag* : `v1.15.0` — *Publish release*
   - *Attach binaries* : téléverser `OptiPaie-PRO-Setup.exe`
   > Le `download_url` `.../releases/latest/download/OptiPaie-PRO-Setup.exe` pointe
   > **toujours** vers l'asset de la dernière release — inutile de le changer.

4. **Mettre à jour `version.json`** (racine du dépôt) :
   ```json
   { "latest_version": "1.15.0",
     "download_url": "https://github.com/gmao4m/optipaie-pro/releases/latest/download/OptiPaie-PRO-Setup.exe",
     "release_notes": "• …", "mandatory": false }
   ```

5. **`git push`** (le `version.json` mis à jour part sur `main`).

À la prochaine ouverture, **tous les postes** voient la pop-up et se mettent à jour. ✅

## Test rapide

Pour vérifier la pop-up sans attendre une vraie release : mettre temporairement
`latest_version` à une valeur supérieure à la version installée (ex. `"99.0.0"`) dans le
`version.json` hébergé — la pop-up doit apparaître au démarrage. Remettre la vraie valeur
ensuite. (En build **Debug**, laisser `Update.VersionJsonUrl` renseigné : la vérification
reste silencieuse tant que `latest_version` ≤ version courante.)
