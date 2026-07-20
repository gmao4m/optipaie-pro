using System;
using DevExpress.XtraReports.UI;
using OptiPaie.Core.Dtos;
using OptiPaie.Localization;
using OptiPaie.Reporting.Reports;

namespace OptiPaie.Reporting.Engine
{
    /// <summary>
    /// Builds the payslip report and either previews it or exports it to PDF.
    /// </summary>
    public sealed class ReportService : IReportService
    {
        private readonly ILocalizationService _localization;

        public ReportService(ILocalizationService localization)
        {
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        }

        public void Preview(PayslipPrintModel model)
        {
            using (var report = new PayslipReport(model, _localization))
            using (var tool = new ReportPrintTool(report))
            {
                tool.ShowPreviewDialog();
            }
        }

        public void ExportPdf(PayslipPrintModel model, string filePath)
        {
            using (var report = new PayslipReport(model, _localization))
            {
                report.ExportToPdf(filePath);
            }
        }
    }
}
