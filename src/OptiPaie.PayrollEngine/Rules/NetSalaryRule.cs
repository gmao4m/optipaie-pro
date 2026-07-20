using OptiPaie.Core.Constants;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.PayrollEngine.Pipeline;

namespace OptiPaie.PayrollEngine.Rules
{
    /// <summary>
    /// Finalises the IRG (regular + lissage), sums the net-only deductions
    /// (acompte/avance) and computes the net salary:
    /// Net = Gross − CNAS employee − IRG − net deductions.
    /// </summary>
    internal sealed class NetSalaryRule : IPayrollRule
    {
        public int Order => 800;

        public string Name => "NetSalary";

        public void Apply(PayrollCalculationContext context)
        {
            context.Irg = context.Money.Round(context.IrgRegular + context.IrgLissage);
            context.AddTrace("IRG_TOTAL", context.Irg, "IRG total = IRG régulier + IRG lissé.");

            decimal netDeductions = 0m;
            foreach (WorkingLine line in context.Lines)
            {
                if (line.ElementType == ElementType.Deduction && !line.IsIncludedInGross)
                {
                    netDeductions += line.Amount;
                }
            }

            context.NetDeductions = context.Money.Round(netDeductions);
            context.AddTrace("NET_DEDUCTIONS", context.NetDeductions, "Retenues nettes (acompte, avance).");

            decimal net = context.GrossSalary - context.CnasEmployee - context.Irg - context.NetDeductions;
            context.NetSalaire = context.Money.Round(net);
            context.AddTrace("NET", context.NetSalaire, "Salaire net = brut − CNAS − IRG − retenues nettes.");

            if (context.NetSalaire < 0m)
            {
                context.Messages.Add(PayrollMessage.Error(
                    PayrollErrorCodes.NetNegative,
                    "Le total des retenues dépasse le salaire; le net est négatif."));
            }
        }
    }
}
