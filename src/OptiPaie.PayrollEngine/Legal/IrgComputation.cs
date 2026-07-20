namespace OptiPaie.PayrollEngine.Legal
{
    /// <summary>
    /// The result of an IRG computation for one taxable base: the gross tax from the
    /// barème, the abattement applied, and the final IRG after the exemption /
    /// smoothing rules. All values are rounded per the active rounding policy.
    /// </summary>
    public sealed class IrgComputation
    {
        /// <summary>IRG from the barème, before abattement.</summary>
        public decimal IrgBrut { get; }

        /// <summary>Abattement applied to the gross IRG.</summary>
        public decimal Abattement { get; }

        /// <summary>Final IRG after exemption / smoothing.</summary>
        public decimal Irg { get; }

        public IrgComputation(decimal irgBrut, decimal abattement, decimal irg)
        {
            IrgBrut = irgBrut;
            Abattement = abattement;
            Irg = irg;
        }
    }
}
