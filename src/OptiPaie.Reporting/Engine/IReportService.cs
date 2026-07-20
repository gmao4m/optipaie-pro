using OptiPaie.Core.Dtos;

namespace OptiPaie.Reporting.Engine
{
    /// <summary>Renders payslip documents to a print preview or to PDF.</summary>
    public interface IReportService
    {
        /// <summary>Opens a print preview of the payslip.</summary>
        void Preview(PayslipPrintModel model);

        /// <summary>Exports the payslip to a PDF file.</summary>
        void ExportPdf(PayslipPrintModel model, string filePath);
    }
}
