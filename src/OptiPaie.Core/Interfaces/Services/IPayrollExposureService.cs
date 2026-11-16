using OptiPaie.Core.Payroll;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Builds the read-only exposure diagnostic (PART 3) from the element catalog, so a
    /// customer can see their own risk (exempt allowances with no enforced cap) without
    /// anyone reading their database. Read-only — it changes nothing and never touches the
    /// engine.
    /// </summary>
    public interface IPayrollExposureService
    {
        /// <summary>
        /// Lists every active element exempt from the contribution and/or taxable base, and
        /// every element carrying a (currently unenforced) exemption ceiling.
        /// </summary>
        PayrollExposureReport BuildCatalogExposure();
    }
}
