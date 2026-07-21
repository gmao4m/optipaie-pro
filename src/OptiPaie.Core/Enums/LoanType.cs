namespace OptiPaie.Core.Enums
{
    /// <summary>Kind of money advanced to an employee.</summary>
    public enum LoanType
    {
        /// <summary>Prêt — repaid over several months.</summary>
        Loan = 1,

        /// <summary>Avance sur salaire — usually recovered on the next payslip.</summary>
        Advance = 2
    }
}
