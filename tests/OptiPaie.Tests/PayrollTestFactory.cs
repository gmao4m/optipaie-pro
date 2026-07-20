using System.Collections.Generic;
using System.Linq;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Helpers to build payroll engine inputs for the test library, using the 2026
    /// legal values (CNAS 9% / 26%, SNMG 24 000) and centime rounding.
    /// </summary>
    internal static class PayrollTestFactory
    {
        public const decimal CnasEmployeeRate = 0.09m;
        public const decimal CnasEmployerRate = 0.26m;
        public const decimal Snmg = 24000m;

        public static LegalSnapshot Snapshot()
        {
            return Snapshot(CnasEmployeeRate, CnasEmployerRate, Snmg, RoundingPolicy.Centime);
        }

        public static LegalSnapshot Snapshot(decimal cnasEmployee, decimal cnasEmployer, decimal snmg, RoundingPolicy rounding)
        {
            return new LegalSnapshot(cnasEmployee, cnasEmployer, snmg, rounding);
        }

        public static PayrollContext Context(decimal baseSalary, params PayrollElementInput[] elements)
        {
            return Context(baseSalary, Snapshot(), elements);
        }

        public static PayrollContext Context(decimal baseSalary, LegalSnapshot snapshot, params PayrollElementInput[] elements)
        {
            return new PayrollContext
            {
                Period = new PayrollPeriod(2026, 6),
                BaseSalary = baseSalary,
                WorkedDays = 0m,
                WorkableDays = 0m,
                WorkedHours = 0m,
                Legal = snapshot,
                Elements = elements.ToList()
            };
        }

        public static PayrollElementInput Gain(string label, decimal amount, bool cnas = true, bool irg = true, bool gross = true)
        {
            return new PayrollElementInput
            {
                LabelFr = label,
                LabelAr = label,
                ElementType = ElementType.Gain,
                CalculationMethod = CalculationMethod.FixedAmount,
                Amount = amount,
                IsCnasApplicable = cnas,
                IsIrgApplicable = irg,
                IsIncludedInGross = gross,
                Periodicity = ElementPeriodicity.Monthly
            };
        }

        public static PayrollElementInput Percentage(string label, decimal rate, bool cnas = true, bool irg = true, bool gross = true)
        {
            return new PayrollElementInput
            {
                LabelFr = label,
                LabelAr = label,
                ElementType = ElementType.Gain,
                CalculationMethod = CalculationMethod.Percentage,
                CalculationBase = CalculationBase.SalaireDeBase,
                Rate = rate,
                IsCnasApplicable = cnas,
                IsIrgApplicable = irg,
                IsIncludedInGross = gross,
                Periodicity = ElementPeriodicity.Monthly
            };
        }

        public static PayrollElementInput QuantityUnitPrice(string label, decimal quantity, decimal unitPrice, bool cnas = true, bool irg = true, bool gross = true)
        {
            return new PayrollElementInput
            {
                LabelFr = label,
                LabelAr = label,
                ElementType = ElementType.Gain,
                CalculationMethod = CalculationMethod.QuantityUnitPrice,
                Quantity = quantity,
                UnitPrice = unitPrice,
                IsCnasApplicable = cnas,
                IsIrgApplicable = irg,
                IsIncludedInGross = gross,
                Periodicity = ElementPeriodicity.Monthly
            };
        }

        /// <summary>A non-cotisable, non-taxable allowance counted in the gross (e.g. panier/transport).</summary>
        public static PayrollElementInput NonCotisableGain(string label, decimal amount)
        {
            return Gain(label, amount, cnas: false, irg: false, gross: true);
        }

        /// <summary>An absence: reduces gross, cotisable and taxable bases.</summary>
        public static PayrollElementInput Absence(decimal amount)
        {
            return new PayrollElementInput
            {
                LabelFr = "Retenue Absence",
                LabelAr = "اقتطاع الغياب",
                ElementType = ElementType.Deduction,
                CalculationMethod = CalculationMethod.FixedAmount,
                Amount = amount,
                IsCnasApplicable = true,
                IsIrgApplicable = true,
                IsIncludedInGross = true,
                Periodicity = ElementPeriodicity.Monthly
            };
        }

        /// <summary>A net-only deduction (acompte/avance): affects neither CNAS nor IRG nor gross.</summary>
        public static PayrollElementInput NetDeduction(string label, decimal amount)
        {
            return new PayrollElementInput
            {
                LabelFr = label,
                LabelAr = label,
                ElementType = ElementType.Deduction,
                CalculationMethod = CalculationMethod.FixedAmount,
                Amount = amount,
                IsCnasApplicable = false,
                IsIrgApplicable = false,
                IsIncludedInGross = false,
                Periodicity = ElementPeriodicity.Monthly
            };
        }

        /// <summary>A lissed (non-monthly) gain such as a rappel.</summary>
        public static PayrollElementInput Lissaged(string label, decimal amount, int months, IEnumerable<decimal> referenceBases)
        {
            return new PayrollElementInput
            {
                LabelFr = label,
                LabelAr = label,
                ElementType = ElementType.Gain,
                CalculationMethod = CalculationMethod.FixedAmount,
                Amount = amount,
                IsCnasApplicable = true,
                IsIrgApplicable = true,
                IsIncludedInGross = true,
                Periodicity = ElementPeriodicity.OneOff,
                Lissage = new LissageInput(months, referenceBases)
            };
        }
    }
}
