# Diffuser une nouvelle version — en DEUX étapes séparées

Depuis l'incident du 2026-11-08, publier et **annoncer** sont deux actions distinctes.
Construire une release **ne prévient aucun client**. Prévenir les clients est un **bouton
manuel** que vous pressez vous-même.

## Étape 1 — Construire & publier (automatique sur changement de version)

1. **Incrémente** `<Version>` dans `Directory.Build.props` (ex. `1.40.2`).
2. **Écris** `docs/CHANGELOG-<version>-ar.md` en arabe, destiné au client (sans ce fichier, la publication **échoue**).
3. **`git push`** sur `main`.

Le workflow `.github/workflows/release.yml` se déclenche **uniquement** parce que la version a
changé : il exige le changelog arabe, lance les tests + le smoke test WPF, construit l'installeur
(gardes de complétude **bloquantes**), publie la release (**corps = ce changelog**, jamais la liste
des commits) et vérifie que l'asset est téléchargeable. **Puis il s'arrête.** La release existe,
mais `version.json` n'est **pas** touché : **aucun client n'est encore averti.**

Un push qui ne change pas la version (ou dont le tag existe déjà) ne publie rien.

## Étape 2 — Annoncer aux clients (MANUEL — vous seul)

Quand vous avez validé la release et voulez que les clients la reçoivent :

- GitHub → onglet **Actions** → **Announce update** → **Run workflow** :
  - `version` = ex. `1.40.2` (laisser vide = la `<Version>` actuelle)
  - `confirm` = retaper la **même** chaîne de version
- En ligne de commande : `gh workflow run announce.yml -f version=1.40.2 -f confirm=1.40.2`

`announce.yml` est la **seule** chose du dépôt qui bascule `version.json`. Il refuse d'annoncer si
la release/le tag n'existe pas, si l'asset n'est pas téléchargeable, ou si le changelog arabe manque.
C'est à ce moment-là — et seulement là — que les clients voient la mise à jour à leur prochaine
vérification.

## Revenir en arrière (rollback d'annonce)

Relancer **Announce update** pour la version précédente (sa release existe toujours), ou remettre
`version.json` à la version précédente sur `main` avec `[skip ci]`. Ne **jamais** supprimer la
release publiée : elle reste la référence de diagnostic.
