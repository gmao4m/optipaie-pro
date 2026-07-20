namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// Immutable bundle of the statutory totals produced by the engine, following
    /// the exact stages of the specification: Brut → Cotisable → CNAS → Imposable →
    /// IRG (brut, abattement, final) → Net. Grouping them keeps the result and the
    /// payslip mapping clean.
    /// </summary>
    public sealed class PayrollTotals
    {
        /// <summary>Salaire Brut.</summary>
        public decimal SalaireBrut { get; }

        /// <summary>Base Cotisable.</summary>
        public decimal BaseCotisable { get; }

        /// <summary>CNAS employee contribution.</summary>
        public decimal CnasEmployee { get; }

        /// <summary>CNAS employer contribution (future DAS; not part of net).</summary>
        public decimal CnasEmployer { get; }

        /// <summary>Base Imposable.</summary>
        public decimal BaseImposable { get; }

        /// <summary>IRG before abattement.</summary>
        public decimal IrgBrut { get; }

        /// <summary>Abattement applied to the IRG.</summary>
        public decimal Abattement { get; }

        /// <summary>Final IRG after exemption/smoothing.</summary>
        public decimal Irg { get; }

        /// <summary>Salaire Net à Payer.</summary>
        public decimal NetSalaire { get; }

        /// <summary>Creates an immutable totals bundle.</summary>
        public PayrollTotals(
            decimal salaireBrut,
            decimal baseCotisable,
            decimal cnasEmployee,
            decimal cnasEmployer,
            decimal baseImposable,
            decimal irgBrut,
            decimal abattement,
            decimal irg,
            decimal netSalaire)
        {
            SalaireBrut = salaireBrut;
            BaseCotisable = baseCotisable;
            CnasEmployee = cnasEmployee;
            CnasEmployer = cnasEmployer;
            BaseImposable = baseImposable;
            IrgBrut = irgBrut;
            Abattement = abattement;
            Irg = irg;
            NetSalaire = netSalaire;
        }
    }
}
