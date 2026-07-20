using OptiPaie.Core.Constants;
using OptiPaie.Core.Dtos;
using OptiPaie.PayrollEngine.Pipeline;

namespace OptiPaie.PayrollEngine.Rules
{
    /// <summary>
    /// Regular taxable base = (sum of IRG-applicable, non-lissage lines) − CNAS
    /// employee. Lissage elements are excluded here and taxed separately by the
    /// lissage rule, so they are not taxed at the full monthly marginal rate.
    /// </summary>
    internal sealed class TaxableBaseRule : IPayrollRule
    {
        public int Order => 500;

        public string Name => "TaxableBase";

        public void Apply(PayrollCalculationContext context)
        {
            decimal taxableGains = 0m;

            foreach (WorkingLine line in context.Lines)
            {
                if (line.IsIrgApplicable && !line.IsLissage)
                {
                    // IrgFactor is 1 for a fully-taxable line and 0..1 for a partial one.
                    taxableGains += line.Sign * line.Amount * line.IrgFactor;
                }
            }

            // Taxable gains and CNAS are already rounded, so the base is exact.
            decimal regular = taxableGains - context.CnasEmployee;

            if (regular < 0m)
            {
                regular = 0m;
                context.Messages.Add(PayrollMessage.Warning(
                    PayrollErrorCodes.NegativeTaxableBase,
                    "La base imposable calculée est négative; elle a été ramenée à zéro."));
            }

            context.RegularTaxableBase = regular;
            context.AddTrace("TAXABLE", context.RegularTaxableBase, "Base imposable = gains imposables − CNAS salarié.");
        }
    }
}
