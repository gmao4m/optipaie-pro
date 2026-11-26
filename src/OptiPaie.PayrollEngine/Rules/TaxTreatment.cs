using OptiPaie.PayrollEngine.Pipeline;

namespace OptiPaie.PayrollEngine.Rules
{
    /// <summary>
    /// THE tax-treatment rule of a payslip line, in one place.
    ///
    ///   Base imposable (IRG) = éléments soumis à cotisation − CNAS salarié
    ///                        + éléments imposables non cotisables.
    ///
    ///   | Element                                   | CNAS          | IRG                                  |
    ///   |-------------------------------------------|---------------|--------------------------------------|
    ///   | Cotisable (salaire de base, IEP, IFSP,     | its CNAS share| 100 % — a partial taxable share      |
    ///   |  nuisance, rendement, heures sup., …)      |               |  (10/30/50 %) is NOT allowed on a    |
    ///   |                                           |               |  cotisable element and counts 100 %  |
    ///   | Cotisable, declared "0 % non imposable"   | its CNAS share| 0 % — an explicit legal exemption    |
    ///   | Not cotisable, exempt by law (panier,     | 0             | 0                                    |
    ///   |  transport, déplacement, salaire unique,  |               |                                      |
    ///   |  zone/IFRI as configured)                 |               |                                      |
    ///   | Not cotisable, taxable                    | 0             | its configured share                 |
    ///
    /// Why: a customer's catalog had IEP, IFSP and prime de nuisance — all cotisable — set to
    /// "Imposable 10 %". 90 % of 9 600 (= 8 640) left the IRG base, which fell under the exemption
    /// threshold, and the IRG came out at zero where the accountant's payslip has 353.70
    /// (IrgReferenceCaseTests). A cotisable allowance is taxable; nothing but a legal exemption removes it.
    /// </summary>
    internal static class TaxTreatment
    {
        /// <summary>The share of the line's amount that enters the regular IRG base (0..1).</summary>
        public static decimal IrgShare(WorkingLine line)
        {
            if (!line.IsIrgApplicable)
            {
                return 0m; // not taxable (0 % — exempt by law, as declared on the element)
            }

            if (line.IsCnasApplicable)
            {
                return 1m; // cotisable ⇒ taxable in full: a partial taxable share is ignored
            }

            return line.IrgFactor; // not cotisable: its own configured share
        }
    }
}
