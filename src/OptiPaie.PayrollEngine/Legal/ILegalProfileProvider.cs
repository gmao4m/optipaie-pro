using OptiPaie.Core.Primitives;

namespace OptiPaie.PayrollEngine.Legal
{
    /// <summary>
    /// Selects the legal profile in force for a given payroll period. New Finance
    /// Laws are added as new profiles here, with no change to the calculation code.
    /// </summary>
    public interface ILegalProfileProvider
    {
        /// <summary>Returns the legal profile applicable to the given period.</summary>
        LegalProfile GetProfile(PayrollPeriod period);
    }
}
