using OptiPaie.PayrollEngine.Legal;
using OptiPaie.PayrollEngine.Pipeline;

namespace OptiPaie.PayrollEngine.Rules
{
    /// <summary>
    /// Progressive IRG on the regular taxable base: barème → abattement → exemption
    /// / smoothing, all via the shared <see cref="IrgCalculator"/>. Records the gross
    /// IRG, the abattement and the (regular) IRG for the trace.
    /// </summary>
    internal sealed class IrgRule : IPayrollRule
    {
        public int Order => 600;

        public string Name => "Irg";

        public void Apply(PayrollCalculationContext context)
        {
            IrgComputation computation = context.IrgCalculator.Compute(context.RegularTaxableBase, context.Profile);

            context.IrgBrut = computation.IrgBrut;
            context.Abattement = computation.Abattement;
            context.IrgRegular = computation.Irg;

            context.AddTrace("IRG_BRUT", context.IrgBrut, "IRG brut (barème progressif).");
            context.AddTrace("ABATTEMENT", context.Abattement, "Abattement appliqué sur l'IRG brut.");
            context.AddTrace("IRG_REGULAR", context.IrgRegular, "IRG après abattement / exonération / lissage de tranche.");
        }
    }
}
