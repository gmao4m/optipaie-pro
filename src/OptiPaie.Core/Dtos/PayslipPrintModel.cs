using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// Everything needed to render one payslip document: the company, the employee,
    /// the frozen payslip totals, the detail lines and the language to print in.
    /// </summary>
    public sealed class PayslipPrintModel
    {
        public Company Company { get; set; }
        public Employee Employee { get; set; }
        public Payslip Payslip { get; set; }
        public IReadOnlyList<PayrollDetail> Lines { get; set; }
        public string LanguageCode { get; set; }
        public int PeriodYear { get; set; }
        public int PeriodMonth { get; set; }
    }
}
