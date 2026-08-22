# Diffuser une nouvelle version — c'est tout

1. **Incrémente** `<Version>` dans `Directory.Build.props` (ex. `1.29.2`).
2. **Écris** `docs/CHANGELOG-<version>-ar.md` en arabe, destiné au client (sans ce fichier, la diffusion **échoue**).
3. **`git push`** sur `main`.

Le pipeline (`.github/workflows/release.yml`) se déclenche **uniquement** parce que la version a changé : il exige le changelog arabe, lance les tests, construit l'installeur (gardes de complétude **bloquantes**), publie la release (**corps = ce changelog**, jamais la liste des commits), vérifie que l'asset est téléchargeable publiquement, **puis seulement** bascule `version.json`. Un push qui ne change pas la version ne diffuse rien.
