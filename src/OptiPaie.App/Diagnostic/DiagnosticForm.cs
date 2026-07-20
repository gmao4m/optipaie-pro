using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using OptiPaie.App.Common;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Primitives;
using OptiPaie.PayrollEngine;
using OptiPaie.PayrollEngine.Legal;

namespace OptiPaie.App.Diagnostic
{
    /// <summary>
    /// Hidden maintenance window (Ctrl+Alt+D). Runs a synthetic calculation to
    /// surface engine/legal versions, the active legal profile, the rule execution
    /// order, timing and memory. Not part of the normal user workflow.
    /// </summary>
    public sealed class DiagnosticForm : XtraForm
    {
        private readonly AppServices _services;

        public DiagnosticForm(AppServices services)
        {
            _services = services;
            BuildUi();
            UiHelper.ApplyRightToLeft(this, _services.Localization.IsRightToLeft);
            Text = _services.Localization.GetString("Diagnostic_Title");
        }

        private string T(string key)
        {
            return _services.Localization.GetString(key);
        }

        private void BuildUi()
        {
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(720, 560);

            var memo = new MemoEdit { Dock = DockStyle.Fill };
            memo.Properties.ReadOnly = true;
            memo.Font = new Font("Consolas", 9.5F);
            memo.Text = BuildReport();
            Controls.Add(memo);

            var close = new SimpleButton { Text = T("Common_Close"), Dock = DockStyle.Bottom, Height = 40 };
            UiTheme.SecondaryButton(close);
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }

        private string BuildReport()
        {
            DateTime now = DateTime.Now;
            var period = new PayrollPeriod(now.Year, now.Month);
            LegalProfile profile = new BuiltInLegalProfileProvider().GetProfile(period);
            LegalSnapshot snapshot = _services.ConfigurationService.GetLegalSnapshot();

            var context = new PayrollContext
            {
                Period = period,
                BaseSalary = 60000m,
                Legal = snapshot,
                Elements = new List<PayrollElementInput>()
            };

            var stopwatch = Stopwatch.StartNew();
            PayrollResult result = new PayrollCalculationEngine().Calculate(context);
            stopwatch.Stop();

            var sb = new StringBuilder();
            Line(sb, T("Diagnostic_EngineVersion"), EngineVersion.Version);
            Line(sb, T("Diagnostic_LegalVersion"), profile.LegalVersion + "  (" + profile.EffectiveFrom.ToString("yyyy-MM-dd") + ")");
            Line(sb, T("Diagnostic_CalcVersion"), EngineVersion.CalculationVersion);
            Line(sb, "Base de données", _services.Configuration.DatabaseFilePath);
            sb.AppendLine();

            Line(sb, T("Diagnostic_CnasRate"), (snapshot.CnasEmployeeRate * 100m).ToString("0.##", CultureInfo.InvariantCulture) + " % / " +
                                               (snapshot.CnasEmployerRate * 100m).ToString("0.##", CultureInfo.InvariantCulture) + " %");
            Line(sb, "SNMG", snapshot.Snmg.ToString("N2", CultureInfo.InvariantCulture));
            sb.AppendLine();

            Line(sb, T("Diagnostic_IrgBrackets"), string.Empty);
            foreach (IrgBracket bracket in profile.IrgBrackets)
            {
                string upper = bracket.UpperBound.HasValue
                    ? bracket.UpperBound.Value.ToString("N0", CultureInfo.InvariantCulture)
                    : "∞";
                sb.AppendLine("    " + bracket.LowerBound.ToString("N0", CultureInfo.InvariantCulture) + " - " + upper +
                              "  :  " + (bracket.Rate * 100m).ToString("0.##", CultureInfo.InvariantCulture) + " %");
            }
            sb.AppendLine();

            Line(sb, T("Diagnostic_Abattement"),
                (profile.Abattement.Rate * 100m).ToString("0.##", CultureInfo.InvariantCulture) + " %  [min " +
                profile.Abattement.Min.ToString("N0", CultureInfo.InvariantCulture) + " / max " +
                profile.Abattement.Max.ToString("N0", CultureInfo.InvariantCulture) + "]");
            Line(sb, T("Diagnostic_Lissage"), profile.LissageMethod.ToString());
            sb.AppendLine();

            Line(sb, T("Diagnostic_RuleOrder"), string.Join(" → ", result.Trace.Select(s => s.Key)));
            Line(sb, T("Diagnostic_CalcTime"), stopwatch.Elapsed.TotalMilliseconds.ToString("0.000", CultureInfo.InvariantCulture) + " ms");
            Line(sb, T("Diagnostic_Memory"), (GC.GetTotalMemory(false) / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture) + " MB");
            Line(sb, T("Diagnostic_ElementCount"), result.Lines.Count.ToString(CultureInfo.InvariantCulture));
            Line(sb, T("Diagnostic_Validation"), result.IsSuccess ? "OK" : "ERREUR");

            return sb.ToString();
        }

        private static void Line(StringBuilder sb, string label, string value)
        {
            sb.AppendLine(label.PadRight(28) + " : " + value);
        }
    }
}
