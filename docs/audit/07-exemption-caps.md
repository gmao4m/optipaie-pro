# Exemption caps — authoritative research (PART 1)

Goal: find the governing text for each exemption figure the engine would need, so a cap can later be implemented from law, not from a commercial guide. **A commercial guide is a lead, never a source.** Where no official text confirms a figure, it is marked **UNCONFIRMED** and nothing is implemented on it.

Researched 2026-11-16. Primary sources used: the Algerian tax authority (DGI, `mfdgi.gov.dz`) for IRG/CIDTA; CLEISS (`cleiss.fr`, the French public body that documents foreign social-security systems) for CNAS, as of 1 Jan 2024; Grant Thornton Algeria for the LF 2021. **None of these is the Journal Officiel itself** — the consolidated CIDTA, the current Finance Law and the CNAS assiette circular should still be read before any cap is shipped.

## Summary table

| Item | Figure the engine would need | Confirmed? | Governing text (as found) |
|---|---|---|---|
| IRG general exemption | taxable ≤ 30 000/mo exempt | **CONFIRMED** | CIDTA Arts. 5 & 68, DGI IRG-salaires page |
| Zone / résidence / isolement | IRG exemption **capped at 70% of base salary** | **CONFIRMED (IRG only)** | CIDTA Art. 68 (DGI); LF 2021 (Grant Thornton). **CNAS treatment not confirmed.** |
| Prime de panier | per-day exempt amount (160?), worked vs calendar day | **UNCONFIRMED** | Not named in Arts. 5/68. No official DA figure found. |
| Indemnité de transport | monthly exempt amount (3 000?), proration | **UNCONFIRMED** | Not listed in Arts. 5/68 (only "frais de déplacement ou de mission"). No official DA figure found. |
| Avantage logement | housing treatment | **PARTLY CONFIRMED** | Taxable avantage en nature (CIDTA Arts. 69–73), **except** housing in "zones à promouvoir". Food valued ≥ 400 DA/meal. |
| SNMG amount | 24 000 (product) vs 20 000 | **UNCONFIRMED (which is current)** | CLEISS cites 20 000 (Décret 21-137, eff. 1 Jun 2020). Product uses 24 000 — latest decree not located. |
| SNMG minimum contribution base | floor the assiette to SNMG for full-time? | **UNCONFIRMED → likely NO for salaried CNAS** | CLEISS states **no** minimum base tied to SNMG for the salaried regime (the SNMG floor is a CASNOS/non-salarié rule). |
| Employer CNAS | single 26% (product) vs split | **CONFIRMED DIFFERENT** | CLEISS (1 Jan 2024): **25% employer + 9% employee + 0.5% FNPOS = 34.5%**. Product's 26%/35% is an older figure. |

## Detail per item

### Prime de panier — UNCONFIRMED
Arts. 5/68 (DGI) do **not** name a meal/panier allowance. The commonly-cited **160 DA/day** has no official text behind it in anything found; it appears only in commercial guides. It may be exempt only insofar as it is a genuine *remboursement de frais* (CLEISS lists "remboursements de frais" as excluded from the CNAS assiette), but no per-day cap, and no worked-vs-calendar rule, is confirmed. **Do not implement a panier cap until the Journal Officiel / a CNAS circular gives the figure and the day basis.**

### Indemnité de transport — UNCONFIRMED
Arts. 5/68 list only "**les indemnités allouées pour frais de déplacement ou de mission**" — not a standing monthly transport allowance. The **3 000 DA/month** figure has no official source found; it is guide-level only. Proration rule: unknown. **Do not implement a transport cap yet.**

### Indemnité de zone (résidence / isolement) — CONFIRMED for IRG only
DGI lists "indemnités de résidence et d'isolement, **plafonnées à 70% du salaire de base**", and Grant Thornton attributes the 70% limit to the LF 2021 amendment of Art. 68. So the **IRG** exemption on a zone/residence allowance is the excess-over-70%-of-base being taxable. Whether the **CNAS** assiette applies the same 70% is **not confirmed** (CLEISS excludes "indemnités pour conditions de vie particulières" from the assiette, which may or may not be the same thing). Product today: `I.F.R.I` (#20) exempt from both with no cap; `ZONE` (#21) cotisable, IRG-exempt with no cap. Neither applies the 70%.

### Avantage logement — PARTLY CONFIRMED
Housing is a **taxable avantage en nature** under CIDTA Arts. 69–73, **except** housing provided in "zones à promouvoir". Food is valued at **no less than 400 DA/meal**. Product today: `LOGEMENT` (#19) exempt from both CNAS and IRG — **likely wrong** (housing is generally taxable), but the exact valuation rule needs the official text. UNCONFIRMED on the exact amount.

### SNMG and the minimum contribution base — UNCONFIRMED
- **Amount:** CLEISS (2024 page) still cites SNMG = **20 000 DZD** (Décret présidentiel 21-137, eff. 1 Jun 2020). The product uses **24 000**. A later increase to 24 000 is plausible but the decree was **not located** — confirm the current SNMG from the Journal Officiel.
- **Floor on the assiette:** CLEISS gives **no** minimum contribution base tied to the SNMG for the **salaried** regime (that floor is a CASNOS/non-salarié rule). So the engine computing CNAS on the actual base (no SNMG floor) is **likely correct**. What remains a real concern is a *full-time* salary paid **below** the SNMG — that is a **labour-law** problem (illegal pay), which the Part-2 guard flags, not a contribution-assiette floor.

### Employer CNAS — CONFIRMED DIFFERENT from the product
CLEISS (1 Jan 2024) gives the salaried regime as **34.5% total = 25% employer + 9% employee + 0.50% FNPOS (œuvres sociales)**, with this ventilation (the DAS/DAC breakdown):

| Branch | Employer | Employee | Other |
|---|---:|---:|---:|
| Maladie, maternité, invalidité, décès | 11.5% | 1.5% | — |
| Accidents du travail / maladies prof. | 1.25% | — | — |
| Retraite | 11% | 6.75% | 0.5% FNPOS |
| Retraite anticipée | 0.25% | 0.25% | — |
| Assurance chômage | 1% | 0.5% | — |
| **Total** | **25%** | **9%** | **0.50%** |

(+0.375% each side for CACOBATPH sectors — already handled separately in the product.)

The product stores a **single `CNAS_EMPLOYER_RATE = 0.26`** and total 35%. This differs from the authoritative 25% + 0.5% FNPOS / 34.5%. **This affects only the employer contribution shown on declarations (DAS/DAC) — it does not touch the employee's net or IRG.** Confirm the current rate against the CNAS circular before changing it, since rates have changed over time and sources conflict (one simulator still cites 26%).

## Recommendation
1. **Confirmed enough to act on later:** the 30 000 IRG threshold (already correct in the engine), and the **70%-of-base IRG cap** on zone/residence/isolement allowances.
2. **Not confirmed — do NOT implement:** panier per-day, transport monthly, the exact housing valuation, and the current SNMG amount. Get these from the Journal Officiel / CNAS circular first.
3. **Employer rate:** the product's 26% is likely stale vs 25% + 0.5% FNPOS; it is declaration-only. Verify with CNAS before changing — it does not affect the low-salary IRG symptom.
4. Until the caps are confirmed, the right move is the **Part-2 guard** (stop a silently-wrong payslip) and the **Part-3 exposure diagnostic** (let each customer see their own risk) — neither of which hard-codes an unconfirmed cap.

## Sources
- DGI (Algerian tax authority) — IRG traitements et salaires (Arts. 5 & 68, 30 000 threshold, 70% zone cap, avantages en nature Arts. 69–73): https://www.mfdgi.gov.dz/fr/particuliers/irg-traitements-et-salaires
- CLEISS — Les cotisations en Algérie (34.5% = 25%+9%+0.5% FNPOS, branch ventilation, assiette exclusions, SNMG 20 000 / Décret 21-137): https://www.cleiss.fr/docs/cotisations/algerie.html
- Grant Thornton Algérie — LF 2021 (zone allowance IRG exemption limited to 70% of base): https://www.grantthornton.dz/globalassets/1.-member-firms/algeria/media/pdfs/lf-2021-grant-thornton-vf.pdf
- Commercial guides (LEADS ONLY, not sources — panier 160/day, transport 3000/mo): lamacta.com, legal-doctrine.com, almawarid.app.
