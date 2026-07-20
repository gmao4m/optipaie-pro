# OptiPaie DZ — Production Readiness Checklist

Status date: 2026-07-02
Verdict: **NOT production-ready yet.** The UI is a strong release-candidate and the
payroll engine is correct-by-construction (audited against the 2026 rules, no bugs
found), but three gates below can only be closed on your machine / with real data.

---

## 🔴 Hard gates (must all pass before selling to a customer)

### 1. Clean build in Visual Studio
- [ ] Solution builds with **0 errors** (Release, x86).
- [ ] No new warnings introduced by the recent UI / RTL work.
- Recent uncompiled work: Increment 4 (Settings), the full RTL mirroring pass
  (UiTheme `LeadX/PlaceLead/LeadingDock`, page headers, all toolbars, Dashboard,
  Payroll, Settings). LTR is unchanged by design; verify anyway.

### 2. Automated tests green
- [ ] Run the **NUnit payroll-engine suite** (150+ scenarios). All pass.
- [ ] Spot-check the exact spec examples A–F still match to the dinar.

### 3. Legal calculation reconciled to reality (THE critical gate)
The engine matches the *documented* spec, but the spec itself flags these as
**TO CONFIRM** against a real Algerian bulletin / accountant:
- [ ] **Rounding granularity** — centime (current default, scale 2) vs whole-dinar.
      Switch via the `ROUNDING_SCALE` app setting if needed.
- [ ] **Lissage / rappel** — the differential method matches the tax administration's
      practice for a real multi-month rappel.
- [ ] **Abattement / IRG** — reconcile one real payslip end-to-end to the dinar
      (base cotisable → CNAS → base imposable → IRG → abattement → net).
- [ ] Confirm the disabled/retiree supplementary allowance rule if in scope.

---

## 🟠 Should verify before wide release
- [ ] End-to-end workflow: create company → employee → payroll → save → archive →
      PDF export → reprint.
- [ ] Backup + restore round-trip (data intact, app restarts cleanly).
- [ ] Bilingual pass: every screen in **both** French (LTR) and Arabic (RTL) —
      confirm the RTL mirroring landed and nothing clips or overlaps.
- [ ] Payslip PDF reconciled against an official bulletin layout.
- [ ] High-DPI check at 100 / 125 / 150 / 175 / 200 %.
- [ ] Runs on a clean Windows 7/10/11 machine (single EXE, offline, SQLite).

## 🟢 Already in good shape
- Architecture, 4-module scope, offline/single-user model, licensing, first-run
  wizard, commercial visual design system, engine correctness (per spec),
  resx FR/AR balance (288 == 288 keys).

---

## Recommended order
1. Build (gate 1) → fix any compile issues.
2. Run tests (gate 2).
3. Reconcile ONE real payslip to the dinar (gate 3) — this is what actually decides
   production readiness. If numbers match, you are essentially shippable.
4. Do the 🟠 verification pass.
