using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Desktop.Documents
{
    /// <summary>One printed line of the Fiche de Paie (pre-formatted strings + amounts).</summary>
    public sealed class FicheLineModel
    {
        public string Code { get; set; }
        public string Label { get; set; }
        public string BaseText { get; set; }
        public string TauxText { get; set; }
        public decimal? Gain { get; set; }
        public decimal? Retenue { get; set; }
    }

    /// <summary>
    /// Everything the Fiche de Paie document needs to render, gathered from a payroll
    /// result or an archived payslip. The document itself computes nothing.
    /// </summary>
    public sealed class FichePaieModel
    {
        public Company Company { get; set; }
        public Employee Employee { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public bool IsArabic { get; set; }

        public decimal SalaireBrut { get; set; }
        public decimal BaseCotisable { get; set; }
        public decimal CnasEmployee { get; set; }
        public decimal BaseImposable { get; set; }
        public decimal IrgBrut { get; set; }
        public decimal Abattement { get; set; }
        public decimal Irg { get; set; }
        public decimal Lissage { get; set; }
        public decimal NetSalaire { get; set; }
        public decimal WorkedDays { get; set; }

        public List<FicheLineModel> Lines { get; set; } = new List<FicheLineModel>();
    }
}
