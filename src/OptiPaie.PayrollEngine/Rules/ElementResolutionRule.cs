using System;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.PayrollEngine.ElementCalculation;
using OptiPaie.PayrollEngine.Pipeline;

namespace OptiPaie.PayrollEngine.Rules
{
    /// <summary>
    /// Resolves the effective base salary (prorated for partial months) and computes
    /// every element amount, producing the working lines the later rules sum over.
    /// The base salary is intrinsic (always cotisable, taxable and in the gross).
    /// </summary>
    internal sealed class ElementResolutionRule : IPayrollRule
    {
        public const string BaseSalaryLabelFr = "Salaire de base";
        public const string BaseSalaryLabelAr = "الأجر القاعدي";

        private readonly ElementCalculator _elementCalculator;

        public ElementResolutionRule(ElementCalculator elementCalculator)
        {
            _elementCalculator = elementCalculator ?? throw new ArgumentNullException(nameof(elementCalculator));
        }

        public int Order => 100;

        public string Name => "ElementResolution";

        public void Apply(PayrollCalculationContext context)
        {
            PayrollContext input = context.Input;

            decimal effectiveBase = input.BaseSalary;
            if (input.WorkableDays > 0m && input.WorkedDays >= 0m && input.WorkedDays < input.WorkableDays)
            {
                effectiveBase = input.BaseSalary * input.WorkedDays / input.WorkableDays;
            }

            // The base salary line is a payable amount: round it once at source so the
            // gross/cotisable/taxable sums stay exact without re-rounding.
            effectiveBase = context.Money.Round(effectiveBase);
            context.EffectiveBaseSalary = effectiveBase;

            context.Lines.Add(new WorkingLine
            {
                ElementId = null,
                LabelFr = BaseSalaryLabelFr,
                LabelAr = BaseSalaryLabelAr,
                ElementType = ElementType.Gain,
                Amount = effectiveBase,
                IsCnasApplicable = true,
                IsIrgApplicable = true,
                CnasFactor = 1m,
                IrgFactor = 1m,
                IsIncludedInGross = true,
                IsLissage = false,
                DisplayOrder = 0
            });

            context.AddTrace("BASE", effectiveBase, "Salaire de base (proraté si mois partiel).");

            int fallbackOrder = 1;
            foreach (PayrollElementInput element in input.Elements)
            {
                decimal amount = context.Money.Round(_elementCalculator.Calculate(element, effectiveBase));

                bool usesBase = element.CalculationMethod == CalculationMethod.Percentage
                                || element.CalculationMethod == CalculationMethod.BaseRate;

                context.Lines.Add(new WorkingLine
                {
                    ElementId = element.ElementId,
                    LabelFr = element.LabelFr,
                    LabelAr = element.LabelAr,
                    ElementType = element.ElementType,
                    Base = usesBase ? (decimal?)effectiveBase : null,
                    Rate = element.Rate,
                    Quantity = element.Quantity,
                    UnitPrice = element.UnitPrice,
                    Amount = amount,
                    IsCnasApplicable = element.IsCnasApplicable,
                    IsIrgApplicable = element.IsIrgApplicable,
                    CnasFactor = element.CnasFactor,
                    IrgFactor = element.IrgFactor,
                    IsIncludedInGross = element.IsIncludedInGross,
                    IsLissage = element.Lissage != null,
                    Lissage = element.Lissage,
                    DisplayOrder = element.DisplayOrder > 0 ? element.DisplayOrder : fallbackOrder
                });

                fallbackOrder++;
            }
        }
    }
}
