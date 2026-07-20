using OptiPaie.Core.Dtos;
using OptiPaie.PayrollEngine.Pipeline;

namespace OptiPaie.PayrollEngine.Rules
{
    /// <summary>
    /// Employee and employer CNAS = cotisable base × the respective rate (from the
    /// configurable legal snapshot). Only the employee share affects the net; the
    /// employer share is stored for declarations.
    /// </summary>
    internal sealed class CnasRule : IPayrollRule
    {
        public int Order => 400;

        public string Name => "Cnas";

        public void Apply(PayrollCalculationContext context)
        {
            LegalSnapshot legal = context.Input.Legal;

            context.CnasEmployee = context.Money.Round(context.CotisableBase * legal.CnasEmployeeRate);
            context.CnasEmployer = context.Money.Round(context.CotisableBase * legal.CnasEmployerRate);

            context.AddTrace("CNAS", context.CnasEmployee, "CNAS salarié = base cotisable × taux salarial.");
        }
    }
}
