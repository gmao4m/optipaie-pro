using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Core.Constants;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Engine;
using OptiPaie.Core.Primitives;
using OptiPaie.PayrollEngine.ElementCalculation;
using OptiPaie.PayrollEngine.Legal;
using OptiPaie.PayrollEngine.Money;
using OptiPaie.PayrollEngine.Pipeline;
using OptiPaie.PayrollEngine.Rules;
using OptiPaie.PayrollEngine.Validation;

namespace OptiPaie.PayrollEngine
{
    /// <summary>
    /// The payroll calculation engine: a pure, deterministic implementation of the
    /// approved specification. It validates the context, runs the ordered rule
    /// pipeline, verifies the result and returns a complete <see cref="PayrollResult"/>.
    /// No I/O, no UI, no persistence.
    /// </summary>
    public sealed class PayrollCalculationEngine : IPayrollEngine
    {
        private const int MinYear = 2000;
        private const int MaxYear = 2100;

        private readonly ILegalProfileProvider _profileProvider;
        private readonly PayrollContextValidator _validator;
        private readonly IReadOnlyList<IPayrollRule> _rules;

        /// <summary>Creates an engine wired with the built-in defaults.</summary>
        public PayrollCalculationEngine()
        {
            var elementCalculator = new ElementCalculator();
            _profileProvider = new BuiltInLegalProfileProvider();
            _validator = new PayrollContextValidator(elementCalculator);
            _rules = BuildDefaultRules(elementCalculator);
        }

        /// <summary>Creates an engine with explicit collaborators (for testing/extension).</summary>
        public PayrollCalculationEngine(
            ILegalProfileProvider profileProvider,
            ElementCalculator elementCalculator)
        {
            if (elementCalculator == null)
            {
                throw new ArgumentNullException(nameof(elementCalculator));
            }

            _profileProvider = profileProvider ?? throw new ArgumentNullException(nameof(profileProvider));
            _validator = new PayrollContextValidator(elementCalculator);
            _rules = BuildDefaultRules(elementCalculator);
        }

        public PayrollResult Calculate(PayrollContext context)
        {
            // --- Pre-checks that must pass before a legal profile can even be selected ---
            if (context == null)
            {
                return PayrollResult.Failed(
                    new[] { PayrollMessage.Error(PayrollErrorCodes.ContextMissing, "Le contexte de paie est absent.") },
                    EngineVersion.Version, string.Empty, EngineVersion.CalculationVersion);
            }

            var messages = new List<PayrollMessage>();

            if (!IsValidPeriod(context.Period))
            {
                messages.Add(PayrollMessage.Error(PayrollErrorCodes.PeriodInvalid, "La période de paie est invalide."));
            }

            if (context.Legal == null)
            {
                messages.Add(PayrollMessage.Error(PayrollErrorCodes.LegalMissing, "Les paramètres légaux ne sont pas chargés."));
            }

            if (messages.Any(m => m.Severity == PayrollMessageSeverity.Error))
            {
                return PayrollResult.Failed(messages, EngineVersion.Version, string.Empty, EngineVersion.CalculationVersion);
            }

            LegalProfile profile = _profileProvider.GetProfile(context.Period);

            // --- Full validation ---
            messages.AddRange(_validator.Validate(context));
            if (messages.Any(m => m.Severity == PayrollMessageSeverity.Error))
            {
                return PayrollResult.Failed(messages, EngineVersion.Version, profile.LegalVersion, EngineVersion.CalculationVersion);
            }

            // --- Calculation ---
            var money = new MoneyEngine(context.Legal.Rounding);
            var irgCalculator = new IrgCalculator(money);
            var calc = new PayrollCalculationContext(context, profile, money, irgCalculator);

            foreach (PayrollMessage warning in messages)
            {
                calc.Messages.Add(warning);
            }

            foreach (IPayrollRule rule in _rules)
            {
                rule.Apply(calc);
            }

            return BuildResult(calc, profile);
        }

        private static IReadOnlyList<IPayrollRule> BuildDefaultRules(ElementCalculator elementCalculator)
        {
            var rules = new List<IPayrollRule>
            {
                new ElementResolutionRule(elementCalculator),
                new GrossSalaryRule(),
                new CotisableBaseRule(),
                new CnasRule(),
                new TaxableBaseRule(),
                new IrgRule(),
                new LissageRule(),
                new NetSalaryRule()
            };

            return rules.OrderBy(rule => rule.Order).ToList();
        }

        private static bool IsValidPeriod(PayrollPeriod period)
        {
            return period.Year >= MinYear && period.Year <= MaxYear &&
                   period.Month >= 1 && period.Month <= 12;
        }

        private static PayrollResult BuildResult(PayrollCalculationContext calc, LegalProfile profile)
        {
            var totals = new PayrollTotals(
                calc.GrossSalary,
                calc.CotisableBase,
                calc.CnasEmployee,
                calc.CnasEmployer,
                calc.RegularTaxableBase,
                calc.IrgBrut,
                calc.Abattement,
                calc.Irg,
                calc.NetSalaire);

            var lines = new List<PayrollLineResult>(calc.Lines.Count);
            foreach (WorkingLine line in calc.Lines)
            {
                lines.Add(new PayrollLineResult(
                    line.ElementId,
                    line.LabelFr,
                    line.LabelAr,
                    line.ElementType,
                    line.Base,
                    line.Rate,
                    line.Quantity,
                    line.UnitPrice,
                    line.Amount,
                    line.IsCnasApplicable,
                    line.IsIrgApplicable,
                    line.DisplayOrder));
            }

            return new PayrollResult(
                totals,
                lines,
                calc.Trace,
                calc.Messages,
                EngineVersion.Version,
                profile.LegalVersion,
                EngineVersion.CalculationVersion,
                DateTime.UtcNow);
        }
    }
}
