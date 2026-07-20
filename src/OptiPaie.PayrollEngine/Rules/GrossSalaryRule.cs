using OptiPaie.PayrollEngine.Pipeline;

namespace OptiPaie.PayrollEngine.Rules
{
    /// <summary>Gross salary = sum of the lines flagged "included in gross" (gains add, deductions subtract).</summary>
    internal sealed class GrossSalaryRule : IPayrollRule
    {
        public int Order => 200;

        public string Name => "GrossSalary";

        public void Apply(PayrollCalculationContext context)
        {
            decimal gross = 0m;

            foreach (WorkingLine line in context.Lines)
            {
                if (line.IsIncludedInGross)
                {
                    gross += line.Sign * line.Amount;
                }
            }

            // Exact sum of already-rounded line amounts: no additional rounding needed.
            context.GrossSalary = gross;
            context.AddTrace("GROSS", context.GrossSalary, "Salaire brut = somme des gains inclus dans le brut.");
        }
    }
}
