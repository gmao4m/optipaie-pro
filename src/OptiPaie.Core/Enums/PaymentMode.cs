namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// How the employee's net salary is paid.
    /// </summary>
    public enum PaymentMode
    {
        /// <summary>Bank transfer (virement) to the employee's RIB.</summary>
        BankTransfer = 1,

        /// <summary>Cash (espèces).</summary>
        Cash = 2,

        /// <summary>Cheque.</summary>
        Cheque = 3
    }
}
