using OptiPaie.PayrollEngine.Pipeline;

namespace OptiPaie.PayrollEngine.Rules
{
    /// <summary>Cotisable base = sum of the CNAS-applicable lines (gains add, deductions subtract).</summary>
    internal sealed class CotisableBaseRule : IPayrollRule
    {
        public int Order => 300;

        public string Name => "CotisableBase";

        public void Apply(PayrollCalculationContext context)
        {
            decimal cotisable = 0m;

            foreach (WorkingLine line in context.Lines)
            {
                if (line.IsCnasApplicable)
                {
                    // CnasFactor is 1 for a fully-cotisable line and 0..1 for a partial
                    // one, so existing full/none elements are unchanged.
                    cotisable += line.Sign * line.Amount * line.CnasFactor;
                }
            }

            if (cotisable < 0m)
            {
                cotisable = 0m;
            }

            // Exact sum of already-rounded line amounts: no additional rounding needed.
            context.CotisableBase = cotisable;
            context.AddTrace("COTISABLE", context.CotisableBase, "Base cotisable = somme des éléments soumis à la CNAS.");
        }
    }
}
