using System;
using System.Collections.Generic;
using OptiPaie.Core.Constants;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.PayrollEngine.ElementCalculation;

namespace OptiPaie.PayrollEngine.Validation
{
    /// <summary>
    /// Validates the calculation context before any money is computed: legal snapshot
    /// present, base salary and worked days sane, no duplicate elements, supported
    /// calculation methods/bases, and no negative amounts. Any error stops the
    /// calculation (the engine returns a failed result).
    /// </summary>
    internal sealed class PayrollContextValidator
    {
        private readonly ElementCalculator _elementCalculator;

        public PayrollContextValidator(ElementCalculator elementCalculator)
        {
            _elementCalculator = elementCalculator ?? throw new ArgumentNullException(nameof(elementCalculator));
        }

        public List<PayrollMessage> Validate(PayrollContext context)
        {
            var messages = new List<PayrollMessage>();

            if (context.Legal == null)
            {
                messages.Add(PayrollMessage.Error(PayrollErrorCodes.LegalMissing,
                    "Les paramètres légaux ne sont pas chargés."));
            }

            if (context.Elements == null)
            {
                messages.Add(PayrollMessage.Error(PayrollErrorCodes.ElementsMissing,
                    "La liste des rubriques de paie est absente."));
                return messages;
            }

            if (context.BaseSalary < 0m)
            {
                messages.Add(PayrollMessage.Error(PayrollErrorCodes.BaseSalaryNegative,
                    "Le salaire de base ne peut pas être négatif."));
            }

            if (context.WorkableDays < 0m || context.WorkedDays < 0m ||
                (context.WorkableDays > 0m && context.WorkedDays > context.WorkableDays))
            {
                messages.Add(PayrollMessage.Error(PayrollErrorCodes.WorkedDaysInvalid,
                    "Les jours travaillés/ouvrables sont invalides."));
            }

            var seenElementIds = new HashSet<long>();

            foreach (PayrollElementInput element in context.Elements)
            {
                if (element.ElementId.HasValue && !seenElementIds.Add(element.ElementId.Value))
                {
                    messages.Add(PayrollMessage.Error(PayrollErrorCodes.DuplicateElement,
                        "Une rubrique de paie est dupliquée: " + element.LabelFr));
                }

                if (!_elementCalculator.Supports(element.CalculationMethod))
                {
                    messages.Add(PayrollMessage.Error(PayrollErrorCodes.UnsupportedCalculationMethod,
                        "Méthode de calcul non prise en charge pour: " + element.LabelFr));
                }

                if (element.CalculationMethod == CalculationMethod.Percentage ||
                    element.CalculationMethod == CalculationMethod.BaseRate)
                {
                    if (element.CalculationBase == null)
                    {
                        messages.Add(PayrollMessage.Error(PayrollErrorCodes.MissingCalculationBase,
                            "Base de calcul requise pour: " + element.LabelFr));
                    }
                    else if (element.CalculationBase != CalculationBase.SalaireDeBase)
                    {
                        messages.Add(PayrollMessage.Error(PayrollErrorCodes.UnsupportedCalculationBase,
                            "Seule la base 'Salaire de base' est prise en charge pour: " + element.LabelFr));
                    }
                }

                if (IsNegative(element.Amount) || IsNegative(element.Rate) ||
                    IsNegative(element.Quantity) || IsNegative(element.UnitPrice))
                {
                    messages.Add(PayrollMessage.Error(PayrollErrorCodes.NegativeAmount,
                        "Valeur négative non autorisée pour: " + element.LabelFr));
                }
            }

            return messages;
        }

        private static bool IsNegative(decimal? value)
        {
            return value.HasValue && value.Value < 0m;
        }
    }
}
