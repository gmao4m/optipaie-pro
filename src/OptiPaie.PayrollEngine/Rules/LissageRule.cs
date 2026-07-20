using System.Collections.Generic;
using OptiPaie.Core.Constants;
using OptiPaie.Core.Dtos;
using OptiPaie.PayrollEngine.Pipeline;

namespace OptiPaie.PayrollEngine.Rules
{
    /// <summary>
    /// Lissage (specification §6): for each non-monthly element, the IRG on the
    /// spread amount is the sum over the concerned months of
    /// IRG(monthBase + share) − IRG(monthBase), where share = amount / months and the
    /// month bases are supplied by the caller (falling back to the current taxable
    /// base when not provided).
    /// </summary>
    internal sealed class LissageRule : IPayrollRule
    {
        public int Order => 700;

        public string Name => "Lissage";

        public void Apply(PayrollCalculationContext context)
        {
            decimal total = 0m;
            bool hasLissage = false;

            foreach (WorkingLine line in context.Lines)
            {
                if (!line.IsLissage || !line.IsIrgApplicable || line.Lissage == null)
                {
                    continue;
                }

                hasLissage = true;

                LissageInput lissage = line.Lissage;
                int months = lissage.Months < 1 ? 1 : lissage.Months;
                decimal share = line.Amount / months;
                IReadOnlyList<decimal> referenceBases = lissage.ReferenceBases;

                if (referenceBases.Count == 0)
                {
                    context.Messages.Add(PayrollMessage.Info(
                        PayrollErrorCodes.LissageReferenceMissing,
                        "Aucune base de référence fournie pour le lissage; la base imposable du mois courant a été utilisée."));
                }

                for (int month = 0; month < months; month++)
                {
                    decimal monthBase = month < referenceBases.Count
                        ? referenceBases[month]
                        : context.RegularTaxableBase;

                    decimal withShare = context.IrgCalculator.Compute(monthBase + share, context.Profile).Irg;
                    decimal withoutShare = context.IrgCalculator.Compute(monthBase, context.Profile).Irg;

                    total += withShare - withoutShare;
                }
            }

            context.IrgLissage = context.Money.Round(total);

            if (hasLissage)
            {
                context.AddTrace("LISSAGE", context.IrgLissage, "IRG sur montants lissés (méthode différentielle).");
            }
        }
    }
}
