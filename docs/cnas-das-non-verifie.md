# DAS CNAS — ce qui reste NON VÉRIFIÉ

*À lire le jour où un client te dit « mon dépôt DAS a été refusé ». Ce document te dit,
pour chaque point incertain : ce qu'on **sait**, ce qu'on **suppose**, et ce qu'un **premier
dépôt réel** révélerait. Tout le format a été reconstruit par analyse de **deux fichiers
réels de 2021, d'une seule entreprise** — c'est solide, mais ça n'a jamais été confronté au
portail CNAS actuel.*

*Bonne nouvelle avant de commencer : le logiciel **refuse d'écrire un fichier douteux** plutôt
que d'en produire un faux. Un refus n'est donc pas une panne — c'est le logiciel qui te protège
d'une déclaration incorrecte. Chaque point ci-dessous est un endroit où un vrai dépôt nous
apprendrait quelque chose qu'on ne peut pas savoir aujourd'hui.*

---

## 1. ⚠️ LE PLAFOND DE 99 999,99 DA PAR TRIMESTRE — le point qui peut bloquer des clients réels

**La conséquence commerciale, noir sur blanc :**
> Le champ « salaire du trimestre » dans le fichier fait **7 caractères**, soit **99 999,99 DA
> maximum par trimestre et par salarié**. Cela fait environ **33 000 DA/mois** sur un trimestre
> plein. **Toute entreprise ayant ne serait-ce qu'UN salarié au-dessus de ~33 000 DA/mois verra
> son export refusé.** Beaucoup d'entreprises algériennes réelles sont concernées (cadres,
> techniciens qualifiés, direction).

- **Ce qu'on sait (prouvé) :** dans les fichiers 2021, le champ fait bien 7 caractères. On l'a
  démontré arithmétiquement : les 16 salaires nuls du fichier sont tous calés sur le bord droit
  d'un champ de 7 caractères (jamais 8, 9 ou 10). Le montant le plus élevé du fichier réel
  (84 000 DA/trimestre) occupait déjà **84 %** du champ.
- **Ce qu'on suppose :** que le format actuel du portail a peut-être **élargi** ce champ depuis
  2021. Nos deux fichiers ne contiennent que des salaires modestes — ils ne peuvent pas nous dire
  si un montant plus grand serait accepté. On n'en a **aucune preuve**, ni dans un sens ni dans
  l'autre.
- **Ce qu'un premier dépôt réel révélerait :** un client avec un salarié à salaire élevé est
  **exactement la preuve qui nous manque**. S'il dépose et que le portail **accepte** un montant
  large → le champ est plus grand, et on l'élargit (voir « comment corriger » plus bas). S'il
  **refuse** → notre limite de 7 caractères était juste, et le problème est ailleurs.
- **Ce que le logiciel fait en attendant :** il refuse l'export, **sans jamais tronquer** le
  montant (un montant tronqué produirait une déclaration fausse), et affiche un message clair qui
  invite le client à nous contacter avec le cas. C'est notre canal pour découvrir la vraie largeur.

---

## 2. L'anomalie d'alignement à l'offset 516

- **Ce qu'on sait :** en reconstruisant les fichiers octet par octet, **une seule cellule** sur
  ~1 600 octets ne collait pas : ligne 3, salaire du 3ᵉ trimestre (933 333), cadrée à **gauche**
  dans le fichier réel alors que **tout le reste** (durées, totaux, zéros) est cadré à **droite**.
- **Ce qu'on suppose :** que c'est une **anomalie du logiciel d'origine** qui a produit le fichier
  2021, pas la règle. Onze cellules contre une : la règle est « cadré à droite ».
- **L'arbitrage retenu (2026-08-03) :** notre encodeur cadre **à droite** (la règle prouvée). On
  ne reproduit **pas** l'anomalie. Le test « juge de paix » marque cette cellule précise comme
  exception connue et documentée — il ne la masque pas.
- **Ce qu'un premier dépôt réel révélerait :** si jamais le portail exigeait ce cadrage à gauche
  (très improbable), un dépôt le montrerait. À ce jour, rien n'indique que ce soit le cas.

---

## 3. La zone dénomination / adresse de l'entête — remplie d'espaces

- **Ce qu'on sait :** dans l'entête, une large zone (109 caractères, positions 20 à 128, prévue
  pour la raison sociale et l'adresse) est **entièrement vide** dans les deux fichiers réels.
- **Ce qu'on suppose :** que « vide = 109 espaces » est une configuration acceptée — c'est la
  **seule** qu'on ait vue fonctionner. On ne connaît pas les frontières internes de cette zone
  (où finit le nom, où commence l'adresse).
- **Ce qu'un premier dépôt réel révélerait :** si le portail **exige** un nom/une adresse, un
  dépôt le refuserait avec un message à ce sujet. On a laissé la zone **paramétrable** : si besoin,
  on la remplira sans toucher au reste.

---

## 4. L'indicateur de fin de ligne à « 0 » — rôle inconnu

- **Ce qu'on sait :** chaque ligne de détail se termine par un caractère valant **« 0 »** dans les
  fichiers réels.
- **Ce qu'on suppose :** rien. On ne sait pas ce qu'il représente (un statut ? un type de ligne ?).
  On **reproduit « 0 »** à l'identique parce que c'est ce que le fichier accepté contenait.
- **Ce qu'un premier dépôt réel révélerait :** si ce caractère devait varier selon le salarié
  (par ex. « 1 » dans un cas particulier), seul un fichier réel présentant ce cas nous l'apprendrait.
  C'est isolé comme constante « à confirmer » — un seul endroit à changer le jour où on saura.

---

## 5. Le repli à 173,33 h/mois quand les heures ne sont pas pointées

- **Ce qu'on sait :** le fichier déclare une **durée en heures** par trimestre (unité « H »).
  Quand l'entreprise pointe les heures, on utilise les heures réelles.
- **Ce qu'on suppose :** quand aucune heure n'est pointée, on **estime** à **173,33 h/mois**
  (= 40 h/semaine légales, ce qui reproduit les 520 h d'un trimestre plein observées en 2021).
  C'est une **estimation**, pas une donnée réelle.
- **Ce que le logiciel fait :** il **signale nominativement** chaque salarié dont la durée est
  estimée, **avant** l'export — l'utilisateur le sait et l'export reste possible.
- **Ce qu'un premier dépôt réel révélerait :** si le portail contrôle la cohérence heures/salaire,
  un dépôt nous dirait si notre estimation est acceptable ou s'il faut exiger un vrai pointage.

---

## 6. Tout le format vient de DEUX fichiers de 2021, d'UNE seule entreprise

- **Ce qu'on sait :** la spécification complète (longueurs, positions, unités, séparateurs,
  encodage ASCII sans BOM, fins de ligne) a été **dérivée par analyse binaire** de l'entête et du
  détail d'un employeur réel, année 2021. On reproduit ces deux fichiers **à l'octet près** (sauf
  l'anomalie du point 2).
- **Ce qu'on suppose :** que ce format est **représentatif** de ce que le portail attend
  aujourd'hui. Une seule entreprise, une seule année : on n'a **pas** vu la diversité des cas
  (gros salaires, mouvements multiples, caractères spéciaux, autres centres payeurs).
- **Ce qu'un premier dépôt réel révélerait :** presque tout. Le premier dépôt accepté chez un
  vrai client est la **validation** qui manque à ce module. Jusque-là, chaque bandeau « vérifiez
  le premier dépôt » dans le logiciel dit la vérité.

---

## Comment corriger, le jour où on saura

La conception protège l'avenir : **toute la spec (largeur des champs, alignement, unité, centre
payeur) est une donnée dans un seul fichier** (`DasFileSpec.cs`), pas du code éparpillé.

- Champ trimestriel plus large → changer **un seul nombre** (la longueur du champ). L'encodeur et
  le test juge de paix ne bougent pas.
- Zone dénomination à remplir → la remplir (elle est déjà paramétrable).
- Indicateur de fin différent → changer **une constante**.

En clair : le jour où un vrai dépôt nous apprend la vérité, la correction est **une valeur à
changer**, pas une réécriture. C'est pour ça qu'on a construit le module ainsi.
