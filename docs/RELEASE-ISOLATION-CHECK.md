# Passe adversariale d'isolation inter-sociétés — OBLIGATOIRE avant chaque diffusion

> **Règle absolue.** OptiPaie PRO est multi-sociétés. Sous un sélecteur « société active »,
> **tout** ce qui s'affiche, s'imprime ou s'exporte doit concerner **cette seule société**.
> Une donnée d'une société qui apparaît chez une autre — un nom, un chiffre, une ligne — est
> une **fuite entre clients**. C'est un défaut bloquant, jamais « mineur ».

Cette passe est une **étape fixe du processus de diffusion**, pas une faveur ni une option.
Aucune release n'est publiée (`gh release create` + bascule de `version.json`) tant qu'elle
n'est pas **verte : zéro fuite confirmée**. Elle a déjà payé son coût : elle a trouvé, en
1.29.0, que la cloche de notifications montrait les employés de toutes les sociétés.

---

## 1. Quand

Avant **chaque** diffusion client, une fois le code figé sur `main` et l'installeur construit,
**avant** de créer la release GitHub et de basculer `version.json`.

## 2. Comment — deux volets, les deux obligatoires

### A. Passe automatique (balayage multi-agents)

Lancer une passe adversariale qui **cherche activement** les fuites (elle ne se contente pas de
lire le code modifié : elle balaie tous les points de sortie). Modèle utilisé en 1.29.0 : un
workflow à 5 angles (complétude des écritures d'audit ; tableau de bord / rapports /
notifications ; surface des requêtes UI ; correctifs du diff ; démarrage / rechargement), chaque
trouvaille étant ensuite **vérifiée de façon adversariale** (défaut = « pas une fuite » sauf
preuve de code citant une donnée d'une société atteignant un écran d'une autre).

Critère de retenue d'une trouvaille : **réel = vrai uniquement si** on peut citer le code
prouvant (a) qu'une donnée d'une société atteint une surface affichée sous une **autre** société
active, ou (b) un plantage/régression introduit par le correctif.

### B. Vérification manuelle, surface par surface

Préparer **deux sociétés peuplées différemment** (A et B, données distinctes : employés,
contrats, congés, prêts, actifs, formations, recrutement, présences). Pour **chaque** surface
ci-dessous : ouvrir avec A active, puis basculer sur B, et confirmer que **chaque surface ne
montre QUE la société active** — jamais l'autre, jamais la somme des deux — et que la bascule
recharge tout sans résidu.

## 3. Inventaire des surfaces à vérifier — une par une

| # | Surface de sortie | Où, dans OptiPaie | Invariant à confirmer |
|---|---|---|---|
| 1 | **Écrans de modules** | Employés, Contrats, Congés, Prêts, Présence, Performance, Actifs, Formation, Recrutement, Certificats, Attestations, Archive | La grille/liste ne charge que `GetByCompany(companyId actif)` — jamais un `GetAll` d'une table inter-sociétés |
| 2 | **Tableau de bord (KPIs)** | `DashboardService.Build(companyId)` | `companyId` obligatoire (lève si ≤ 0) ; aucun cumul toutes-sociétés |
| 3 | **Journal d'activité** | `AuditService.GetRecentForCompany(companyId)` | Uniquement les entrées de la société active ; les anciennes (CompanyId NULL) restent exclues |
| 4 | **Cloche de notifications (en-tête)** | `NotificationService.GetNotifications(companyId)` | `companyId` obligatoire ; aucune alerte/nom d'employé d'une autre société |
| 5 | **Rapports** | `ReportService.Build(clé, companyId, année, mois)` | Chaque rapport (effectif, actifs, prêts, masse salariale…) filtré sur `companyId` |
| 6 | **Impressions / PDF** | Fiche de paie (`Documents/`), certificats de travail (`WorkCertificateService`), attestations ATS/DRT (`AtsDrtDocumentService`) | Le document est bâti à partir d'une entité **de la société active uniquement** ; aucun en-tête/logo/employé d'une autre société |
| 7 | **Exports / déclarations** | CNAS **DAS / DAC** (`CnasDeclarationService`) | L'export ne contient que les employés/assiettes de la société active ; nom, NIF, n° employeur = ceux de la société active |
| 8 | **Recherches & grilles** | Toute barre de recherche / DataGrid listant des lignes | La recherche interroge la société active ; aucun résultat d'une autre société |
| 9 | **Listes déroulantes (pickers)** | Sélecteurs d'employé, de département, de contrat, etc. dans les éditeurs | Peuplés via `GetByCompany(companyId actif)` — impossible de choisir l'entité d'une autre société |
| 10 | **Sélecteurs de société** | Sélecteur d'en-tête, écran de choix au démarrage, écran de gestion des sociétés | **Seuls** contrôles légitimement toutes-sociétés. Vérifier qu'aucun **autre** écran n'offre de choisir une société différente de l'active |

### Usages toutes-sociétés LÉGITIMES (ne pas signaler)

- Le **sélecteur de société** de l'en-tête et l'**écran de choix** au démarrage (`CompanyContext.Companies`).
- L'**écran de gestion des sociétés** (créer / modifier / supprimer une société).
- Le **comptage de sociétés** pour la licence (Mono/Multi), qui lit un nombre, pas des données clients.
- Les **anciennes entrées d'audit CompanyId NULL** (antérieures à la migration 0034) : conservées, volontairement exclues de tout journal par société.

## 4. Traitement des trouvailles

- **Fuite confirmée (réel = vrai)** → **bloque la diffusion**. Corriger, ajouter un test
  d'isolation (une société ne voit jamais l'autre), relancer la passe.
- **Écarté (réel = faux)** → consigner la raison (ex. « écran mort/inatteignable », « donnée
  propre de la société sous elle-même »). Un écart n'est pas une correction : le noter.
- **Point de robustesse** (plantage/état incohérent lié à l'isolation, sans fuite) → corriger si
  peu coûteux, sinon documenter et décider explicitement.

## 5. Feu vert

La diffusion n'est autorisée que lorsque la passe est **verte : 0 fuite confirmée**, la suite de
tests complète passe, et les tests d'isolation ajoutés pour chaque correctif sont au vert.
Consigner « passe d'isolation : OK » dans les notes de la release avant `gh release create`.
