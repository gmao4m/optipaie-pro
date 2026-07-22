using System;

namespace OptiPaie.Core.Payroll
{
    /// <summary>
    /// The CACOBATPH contributions for a payslip. Purely additive: computed by
    /// <see cref="CacobatphCalculator"/> from the payslip's existing CNAS base — it never
    /// changes any value produced by the payroll engine.
    /// </summary>
    public sealed class CacobatphResult
    {
        public CacobatphResult(decimal baseAmount, decimal congePaye, decimal chomageEmployer, decimal chomageEmployee)
        {
            Base = baseAmount;
            CongePaye = congePaye;
            ChomageEmployer = chomageEmployer;
            ChomageEmployee = chomageEmployee;
        }

        /// <summary>The base used — the same salaire cotisable the CNAS was computed on.</summary>
        public decimal Base { get; }

        /// <summary>Congé Payé (12,21 %) — an employer cost only.</summary>
        public decimal CongePaye { get; }

        /// <summary>Chômage-Intempéries employer share (0,375 %).</summary>
        public decimal ChomageEmployer { get; }

        /// <summary>Chômage-Intempéries employee share (0,375 %) — deducted from net pay.</summary>
        public decimal ChomageEmployee { get; }

        /// <summary>Total employer cost of CACOBATPH (Congé Payé + Chômage employeur).</summary>
        public decimal EmployerTotal => CongePaye + ChomageEmployer;

        /// <summary>Total deducted from the employee's net pay (the Chômage employee share).</summary>
        public decimal EmployeeTotal => ChomageEmployee;
    }

    /// <summary>
    /// Computes the optional CACOBATPH contributions for a BTPH-sector company, on the
    /// SAME base as the CNAS. This is an additive, read-only overlay: it takes the salaire
    /// cotisable already produced by the payroll engine and never recalculates or mutates
    /// any engine value. When CACOBATPH is disabled it is simply never invoked, so the
    /// payslip is unchanged.
    /// </summary>
    public static class CacobatphCalculator
    {
        /// <summary>Congé Payé rate — 12,21 %, borne entirely by the employer.</summary>
        public const decimal CongePayeRate = 0.1221m;

        /// <summary>Chômage-Intempéries total rate — 0,75 %, split evenly.</summary>
        public const decimal ChomageRate = 0.0075m;

        /// <summary>Chômage-Intempéries employer share — 0,375 %.</summary>
        public const decimal ChomageEmployerRate = 0.00375m;

        /// <summary>Chômage-Intempéries employee share — 0,375 %.</summary>
        public const decimal ChomageEmployeeRate = 0.00375m;

        /// <summary>
        /// Computes the CACOBATPH contributions from the CNAS base (salaire cotisable),
        /// rounded to two decimals. A non-positive base yields zeroes.
        /// </summary>
        public static CacobatphResult Compute(decimal baseCotisable)
        {
            if (baseCotisable <= 0m)
            {
                return new CacobatphResult(0m, 0m, 0m, 0m);
            }

            decimal congePaye = Round(baseCotisable * CongePayeRate);
            decimal chomageEmployer = Round(baseCotisable * ChomageEmployerRate);
            decimal chomageEmployee = Round(baseCotisable * ChomageEmployeeRate);
            return new CacobatphResult(baseCotisable, congePaye, chomageEmployer, chomageEmployee);
        }

        private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
