namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// Who pays a leave type — the information the user SEES on every type and request.
    /// The actual payroll effect stays governed by the company's settings: by default a
    /// CNAS-paid type behaves exactly like an employer-paid one (salary maintained), and a
    /// company may opt in to the strict legal treatment. Nothing here changes a payslip on its own.
    /// </summary>
    public enum PaymentCategory
    {
        /// <summary>مدفوعة من طرف صاحب العمل — paid by the employer (salary maintained).</summary>
        EmployerPaid = 1,

        /// <summary>مدفوعة من طرف الضمان الاجتماعي — paid by CNAS. Informative by default (maps to the
        /// current payroll behaviour); the strict "employer suspends salary" treatment is an opt-in company setting.</summary>
        SocialSecurity = 2,

        /// <summary>غير مدفوعة — unpaid (deducted through attendance as an absence).</summary>
        Unpaid = 3
    }
}
