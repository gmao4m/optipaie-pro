using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using OptiPaie.App.Common;
using OptiPaie.Core.Dtos;

namespace OptiPaie.App.Modules.Payroll
{
    /// <summary>
    /// Visual breakdown of a calculation from gross to net. Each step shows its
    /// localized name, amount and a short explanation, so a payroll officer can
    /// follow exactly how the salary was computed.
    /// </summary>
    public sealed class ExplainForm : XtraForm
    {
        private static readonly Dictionary<string, string> StepKeyToLabel = new Dictionary<string, string>
        {
            { "BASE", "Employee_BaseSalary" },
            { "GROSS", "Totals_Gross" },
            { "COTISABLE", "Totals_Cotisable" },
            { "CNAS", "Totals_Cnas" },
            { "TAXABLE", "Totals_Taxable" },
            { "IRG_BRUT", "Totals_IrgBrut" },
            { "ABATTEMENT", "Totals_Abattement" },
            { "IRG_REGULAR", "Totals_Irg" },
            { "LISSAGE", "Diagnostic_Lissage" },
            { "IRG_TOTAL", "Totals_Irg" },
            { "NET", "Totals_Net" }
        };

        private readonly AppServices _services;

        public ExplainForm(AppServices services, PayrollResult result)
        {
            _services = services;
            BuildUi(result);
            UiHelper.ApplyRightToLeft(this, _services.Localization.IsRightToLeft);
            Text = _services.Localization.GetString("Explain_Title");
        }

        private string T(string key)
        {
            return _services.Localization.GetString(key);
        }

        private void BuildUi(PayrollResult result)
        {
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(760, 560);

            var grid = new GridControl { Dock = DockStyle.Fill };
            Controls.Add(grid);

            var view = new GridView(grid);
            grid.MainView = view;
            view.OptionsBehavior.Editable = false;
            UiTheme.StyleGrid(view);
            view.RowHeight = 36;

            var rows = new List<StepRow>();
            string currency = T("Common_Currency");
            foreach (PayrollCalculationStep step in result.Trace)
            {
                string label = StepKeyToLabel.ContainsKey(step.Key) ? T(StepKeyToLabel[step.Key]) : step.Key;
                rows.Add(new StepRow
                {
                    Step = label,
                    Amount = UiHelper.Money(step.Amount, currency),
                    Explanation = step.Detail
                });
            }

            grid.DataSource = rows;
            view.Columns.Clear();
            view.Columns.AddVisible("Step").Caption = T("Explain_Step");
            view.Columns.AddVisible("Amount").Caption = T("Explain_Amount");
            view.Columns.AddVisible("Explanation").Caption = T("Explain_Description");

            var close = new SimpleButton { Text = T("Common_Close"), Dock = DockStyle.Bottom, Height = 40 };
            UiTheme.SecondaryButton(close);
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }

        private sealed class StepRow
        {
            public string Step { get; set; }
            public string Amount { get; set; }
            public string Explanation { get; set; }
        }
    }
}
