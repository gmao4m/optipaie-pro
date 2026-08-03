using System.Collections.Generic;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// Read-only DAC recap (Déclaration d'Assiette de Cotisation) for ONE company over a
    /// period (a month, or a quarter = 3 months). Aggregates PERSISTED payslips only — it
    /// never runs the payroll engine and modifies no rate. The DAC has no file to submit; it
    /// is a recap the accountant reads from and types by hand on the CNAS portal.
    /// </summary>
    public sealed class CnasDacReport
    {
        public CnasDacReport(
            long companyId, int year, IReadOnlyList<int> months,
            string cnasEmployerNumber, bool employerNumberMissing, bool employerNumberMalformed,
            int effectif, decimal assiette,
            decimal rateSalariale, decimal ratePatronale,
            decimal frozenCnasEmployee, decimal frozenCnasEmployer,
            IReadOnlyList<CnasContributionBranch> officialBranches,
            decimal officialRateSalariale, decimal officialRatePatronale)
        {
            CompanyId = companyId;
            Year = year;
            Months = months ?? new List<int>();
            CnasEmployerNumber = cnasEmployerNumber;
            EmployerNumberMissing = employerNumberMissing;
            EmployerNumberMalformed = employerNumberMalformed;
            Effectif = effectif;
            Assiette = assiette;
            RateSalariale = rateSalariale;
            RatePatronale = ratePatronale;
            FrozenCnasEmployee = frozenCnasEmployee;
            FrozenCnasEmployer = frozenCnasEmployer;
            OfficialBranches = officialBranches ?? new List<CnasContributionBranch>();
            OfficialRateSalariale = officialRateSalariale;
            OfficialRatePatronale = officialRatePatronale;
        }

        public long CompanyId { get; }
        public int Year { get; }

        /// <summary>The period's months (1 for a monthly DAC, 3 for a quarterly one).</summary>
        public IReadOnlyList<int> Months { get; }

        public string CnasEmployerNumber { get; }
        public bool EmployerNumberMissing { get; }
        public bool EmployerNumberMalformed { get; }

        /// <summary>Number of employees with at least one payslip in the period.</summary>
        public int Effectif { get; }

        /// <summary>Assiette cotisable = Σ Payslip.BaseCotisable over the period (frozen data).</summary>
        public decimal Assiette { get; }

        // -- Applied rates: the app's configured CNAS rates (LegalParameters). Shown as-is. --
        public decimal RateSalariale { get; }   // e.g. 0.09
        public decimal RatePatronale { get; }   // e.g. 0.26
        public decimal CotisationSalariale => Assiette * RateSalariale;
        public decimal CotisationPatronale => Assiette * RatePatronale;
        public decimal CotisationTotale => CotisationSalariale + CotisationPatronale;

        // -- Cross-check: the sum of the frozen per-payslip contributions (what payroll produced). --
        public decimal FrozenCnasEmployee { get; }
        public decimal FrozenCnasEmployer { get; }

        // -- Official split (décret 94-187 modifié) — displayed read-only, "à confirmer". --
        public IReadOnlyList<CnasContributionBranch> OfficialBranches { get; }
        public decimal OfficialRateSalariale { get; }   // 0.09
        public decimal OfficialRatePatronale { get; }   // 0.255
        public decimal OfficialCotisationSalariale => Assiette * OfficialRateSalariale;
        public decimal OfficialCotisationPatronale => Assiette * OfficialRatePatronale;

        // -- The gap to arbitrate: applied patronale (config) vs official split. --
        /// <summary>Écart patronal en dinars sur la période (appliqué − officiel).</summary>
        public decimal EcartPatronaleDa => CotisationPatronale - OfficialCotisationPatronale;

        /// <summary>Écart patronal en points de pourcentage (taux appliqué − taux officiel).</summary>
        public decimal EcartPatronalePoints => (RatePatronale - OfficialRatePatronale) * 100m;

        /// <summary>True when the applied patronal rate differs from the official split.</summary>
        public bool HasRateGap => RatePatronale != OfficialRatePatronale;
    }

    /// <summary>One CNAS contribution branch of the official split (rates + amounts on the assiette).</summary>
    public sealed class CnasContributionBranch
    {
        public CnasContributionBranch(string name, decimal patronaleRate, decimal salarialeRate, decimal assiette)
        {
            Name = name;
            PatronaleRate = patronaleRate;
            SalarialeRate = salarialeRate;
            PatronaleAmount = assiette * patronaleRate;
            SalarialeAmount = assiette * salarialeRate;
        }

        public string Name { get; }
        public decimal PatronaleRate { get; }
        public decimal SalarialeRate { get; }
        public decimal PatronaleAmount { get; }
        public decimal SalarialeAmount { get; }
    }
}
