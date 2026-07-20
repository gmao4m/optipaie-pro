using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using DevExpress.Utils;
using DevExpress.XtraPrinting;
using DevExpress.XtraReports.UI;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Localization;

namespace OptiPaie.Reporting.Reports
{
    /// <summary>
    /// FICHE DE PAIE — a fully code-built, premium Algerian payslip (no .repx). Modern,
    /// minimal, corporate: a compact company header with a brand period badge, a bordered
    /// employee panel, an alternating-row payroll table, an invoice-style statutory
    /// summary, a large green "Salaire net à payer" focus block, the amount in words and
    /// a professional signature area (employer / employee / company stamp).
    ///
    /// VISUAL ONLY: it renders values straight from <see cref="PayslipPrintModel"/> and
    /// never computes anything — the engine, rules and totals are untouched. The employer
    /// CNAS charge is intentionally not shown. Arabic is rendered right-aligned with an
    /// Arabic-capable font (XtraReports expresses direction through text alignment).
    /// </summary>
    public sealed class PayslipReport : XtraReport
    {
        // -- Table geometry (report units; usable width = 760) ---------------------
        private const float ColCode = 0f, WCode = 50f;
        private const float ColLabel = 50f, WLabel = 250f;
        private const float ColBase = 300f, WBase = 100f;
        private const float ColTaux = 400f, WTaux = 90f;
        private const float ColGain = 490f, WGain = 135f;
        private const float ColRetenue = 625f, WRetenue = 135f;
        private const float PageWidth = 760f;

        // -- Palette (a single, calm, commercial brand system) ---------------------
        private static readonly Color BrandGreen = Color.FromArgb(19, 123, 80);
        private static readonly Color BrandGreenDark = Color.FromArgb(14, 92, 60);
        private static readonly Color TextDark = Color.FromArgb(33, 41, 51);
        private static readonly Color TextMuted = Color.FromArgb(122, 130, 140);
        private static readonly Color CaptionMuted = Color.FromArgb(140, 148, 158);
        private static readonly Color LineColor = Color.FromArgb(224, 228, 233);
        private static readonly Color EvenRow = Color.FromArgb(246, 249, 247);
        private static readonly Color Deduction = Color.FromArgb(180, 66, 58);
        private static readonly Color OnGreenSoft = Color.FromArgb(206, 232, 220);

        private readonly PayslipPrintModel _model;
        private readonly ILocalizationService _localization;
        private readonly bool _isArabic;
        private readonly string _fontName;

        private readonly List<XRControl> _detailCells = new List<XRControl>();
        private int _rowIndex;

        public PayslipReport(PayslipPrintModel model, ILocalizationService localization)
        {
            _model = model;
            _localization = localization;
            _isArabic = string.Equals(model.LanguageCode, "ar", StringComparison.OrdinalIgnoreCase);
            _fontName = _isArabic ? "Tahoma" : "Segoe UI";

            Build();
        }

        private string T(string key) => _localization.GetString(key);

        private TextAlignment LeadAlign => _isArabic ? TextAlignment.MiddleRight : TextAlignment.MiddleLeft;
        private TextAlignment TrailAlign => _isArabic ? TextAlignment.MiddleLeft : TextAlignment.MiddleRight;

        private void Build()
        {
            Margins = new System.Drawing.Printing.Margins(40, 40, 36, 36);
            DataSource = BuildRows();

            // Reset the alternating-row counter at the start of every render pass.
            BeforePrint += (s, e) => _rowIndex = 0;

            Bands.Add(new TopMarginBand { HeightF = 18 });
            Bands.Add(BuildHeader());
            Bands.Add(BuildDetail());
            Bands.Add(BuildFooter());
            Bands.Add(new BottomMarginBand { HeightF = 18 });
        }

        // ======================================================================
        //  HEADER : company · period badge · title · employee panel · table head
        // ======================================================================

        private ReportHeaderBand BuildHeader()
        {
            var band = new ReportHeaderBand { HeightF = 252 };

            bool hasLogo = AddLogo(band);
            float textX = hasLogo ? 74f : 0f;

            // Company identity (leading side).
            Txt(band, CompanyName(), textX, 2, 470, 24, 14f, FontStyle.Bold, LeadAlign, TextDark);
            Txt(band, CompanyAddress(), textX, 29, 470, 16, 9.5f, FontStyle.Regular, LeadAlign, TextMuted);
            Txt(band, CompanyLegalLine(), textX, 46, 520, 14, 8.5f, FontStyle.Regular, LeadAlign, TextMuted);

            // Period badge (trailing side) — two adjacent labels that OWN their green
            // fill (proven to render: same technique as the green table-header cells).
            XRLabel bcap = Txt(band, T("Archive_Period").ToUpper(Culture), 596, 0, 164, 24, 8f, FontStyle.Bold, TextAlignment.MiddleCenter, OnGreenSoft);
            bcap.BackColor = BrandGreen;
            XRLabel bval = Txt(band, PeriodLabel(), 596, 24, 164, 32, 13.5f, FontStyle.Bold, TextAlignment.MiddleCenter, Color.White);
            bval.BackColor = BrandGreen;

            // Brand accent rule (a filled bar with no text renders correctly).
            Panel(band, 0, 74, PageWidth, 3, BrandGreen, null);

            // Document title.
            Txt(band, T("Payslip_Title"), 0, 88, PageWidth, 32, 22f, FontStyle.Bold, TextAlignment.MiddleCenter, BrandGreen);

            // Employee info — a border-only frame with the fields laid directly on the
            // white band (no fill anywhere that could paint over the text).
            Frame(band, 0, 132, PageWidth, 78);
            Employee e = _model.Employee;
            float c1 = 18, c2 = 278, c3 = 528, fw = 230;
            PanelField(band, T("Payroll_Employee"), EmployeeName(), c1, 144, fw);
            PanelField(band, T("Payslip_Matricule"), MatriculeText(), c2, 144, fw);
            PanelField(band, T("Employee_Poste"), e?.Poste, c3, 144, fw);
            PanelField(band, T("Employee_Category"), e?.Category, c1, 178, fw);
            PanelField(band, T("Employee_HireDate"), HireDate(), c2, 178, fw);
            PanelField(band, T("Employee_Nss"), e?.Nss, c3, 178, fw);

            // Table header row (solid dark-green, white).
            const float hy = 222;
            HeaderCell(band, T("Payslip_Code"), ColCode, hy, WCode, TextAlignment.MiddleCenter);
            HeaderCell(band, T("Payslip_Label"), ColLabel, hy, WLabel, LeadAlign);
            HeaderCell(band, T("Payroll_Base"), ColBase, hy, WBase, TrailAlign);
            HeaderCell(band, T("Payroll_Rate"), ColTaux, hy, WTaux, TrailAlign);
            HeaderCell(band, T("Payslip_Gains"), ColGain, hy, WGain, TrailAlign);
            HeaderCell(band, T("Payslip_Retenues"), ColRetenue, hy, WRetenue, TrailAlign);

            return band;
        }

        // ======================================================================
        //  DETAIL : one compact, alternating row per payroll line
        // ======================================================================

        private DetailBand BuildDetail()
        {
            var band = new DetailBand { HeightF = 23 };

            _detailCells.Clear();
            _detailCells.Add(BoundCell(band, "Code", ColCode, WCode, TextAlignment.MiddleCenter, TextMuted));
            _detailCells.Add(BoundCell(band, "Designation", ColLabel, WLabel, LeadAlign, TextDark));
            _detailCells.Add(BoundCell(band, "Base", ColBase, WBase, TrailAlign, TextDark));
            _detailCells.Add(BoundCell(band, "Taux", ColTaux, WTaux, TrailAlign, TextDark));
            _detailCells.Add(BoundCell(band, "Gain", ColGain, WGain, TrailAlign, TextDark));
            _detailCells.Add(BoundCell(band, "Retenue", ColRetenue, WRetenue, TrailAlign, TextDark));

            // Zebra striping, driven by a render-time counter (reset per pass).
            band.BeforePrint += (s, e) =>
            {
                Color bg = (_rowIndex++ % 2 == 1) ? EvenRow : Color.White;
                foreach (XRControl cell in _detailCells)
                {
                    cell.BackColor = bg;
                }
            };

            return band;
        }

        // ======================================================================
        //  FOOTER : details · statutory summary · NET focus · words · signatures
        // ======================================================================

        private ReportFooterBand BuildFooter()
        {
            var band = new ReportFooterBand { HeightF = 392 };
            Payslip p = _model.Payslip;

            // -- Left: "details" — border-only frame + lines on the band ----------
            Frame(band, 0, 16, 400, 138);
            Txt(band, T("Payslip_Details").ToUpper(Culture), 18, 26, 360, 14, 8f, FontStyle.Bold, LeadAlign, CaptionMuted);
            InfoLine(band, T("Archive_Period"), PeriodLabel(), 50);
            InfoLine(band, T("Payroll_WorkedDays"), WorkedDaysText(), 78);
            InfoLine(band, T("Payslip_IssuedOn"), DateTime.Now.ToString("dd/MM/yyyy", Culture), 106);

            // -- Right: statutory summary — border-only frame + rows on the band --
            Frame(band, 424, 16, 336, 138);
            float sy = 26;
            sy = SummaryRow(band, T("Totals_Gross"), p.SalaireBrut, sy, TextDark, false);
            sy = SummaryRow(band, T("Totals_Cotisable"), p.BaseCotisable, sy, TextMuted, false);
            sy = SummaryRow(band, T("Totals_Cnas"), p.CnasEmployee, sy, Deduction, false);
            sy = SummaryRow(band, T("Totals_Taxable"), p.BaseImposable, sy, TextMuted, false);
            sy = SummaryRow(band, T("Totals_Abattement"), p.Abattement, sy, TextMuted, false);
            sy = SummaryRow(band, T("Totals_Irg"), p.Irg, sy, Deduction, false);

            // -- NET focus block — two adjacent labels that own their green fill ---
            XRLabel netCap = Txt(band, T("Payslip_NetToPay").ToUpper(Culture), 0, 170, 400, 64, 12.5f, FontStyle.Bold, LeadAlign, OnGreenSoft);
            netCap.BackColor = BrandGreen;
            netCap.Padding = new PaddingInfo(24, 8, 0, 0);
            XRLabel netVal = Txt(band, p.NetSalaire.ToString("N2", Culture) + " " + T("Common_Currency"), 400, 170, 360, 64, 25f, FontStyle.Bold, TrailAlign, Color.White);
            netVal.BackColor = BrandGreen;
            netVal.Padding = new PaddingInfo(8, 24, 0, 0);

            // -- Net amount in words -----------------------------------------------
            string words = AmountInWords(p.NetSalaire);
            if (!string.IsNullOrEmpty(words))
            {
                Txt(band, T("Payslip_AmountInWords") + " : " + words,
                    0, 244, PageWidth, 30, 9.5f, FontStyle.Italic, LeadAlign, TextMuted);
            }

            // -- Signatures (employer · employee · company stamp) ------------------
            SignatureSlot(band, T("Payslip_EmployerSignature"), 0);
            SignatureSlot(band, T("Payslip_EmployeeSignature"), 275);
            SignatureSlot(band, T("Payslip_CompanyStamp"), 550);

            // -- Footer note -------------------------------------------------------
            Txt(band, "OptiPaie DZ  ·  " + DateTime.Now.ToString("yyyy-MM-dd", Culture),
                0, 376, PageWidth, 14, 8f, FontStyle.Regular, TextAlignment.MiddleCenter, CaptionMuted);

            return band;
        }

        // ======================================================================
        //  Row projection (pre-formatted strings; no expression bindings)
        // ======================================================================

        private List<PayslipLine> BuildRows()
        {
            var rows = new List<PayslipLine>();
            if (_model.Lines == null)
            {
                return rows;
            }

            int seq = 1;
            foreach (PayrollDetail line in _model.Lines)
            {
                bool gain = line.ElementType == ElementType.Gain;
                rows.Add(new PayslipLine
                {
                    Code = seq.ToString("000", Culture),
                    Designation = _isArabic && !string.IsNullOrWhiteSpace(line.LabelAr) ? line.LabelAr : line.LabelFr,
                    Base = Num(line.Base ?? line.Quantity),
                    Taux = line.Rate.HasValue ? (line.Rate.Value * 100m).ToString("0.##", Culture) + " %" : Num(line.UnitPrice),
                    Gain = gain ? line.Amount.ToString("N2", Culture) : string.Empty,
                    Retenue = gain ? string.Empty : line.Amount.ToString("N2", Culture)
                });
                seq++;
            }

            return rows;
        }

        private static string Num(decimal? value)
        {
            return value.HasValue ? value.Value.ToString("N2", Culture) : string.Empty;
        }

        // ======================================================================
        //  Low-level builders
        // ======================================================================

        private bool AddLogo(Band band)
        {
            if (_model.Company?.Logo == null || _model.Company.Logo.Length == 0)
            {
                return false;
            }

            try
            {
                band.Controls.Add(new XRPictureBox
                {
                    Image = Image.FromStream(new System.IO.MemoryStream(_model.Company.Logo)),
                    Sizing = ImageSizeMode.ZoomImage,
                    LocationFloat = new PointFloat(0, 0),
                    SizeF = new SizeF(64, 64)
                });
                return true;
            }
            catch
            {
                return false; // a corrupt logo must never break the payslip
            }
        }

        private XRPanel Panel(Band band, float x, float y, float w, float h, Color fill, Color? border)
        {
            var panel = new XRPanel
            {
                LocationFloat = new PointFloat(x, y),
                SizeF = new SizeF(w, h),
                BackColor = fill
            };

            if (border.HasValue)
            {
                panel.Borders = BorderSide.All;
                panel.BorderColor = border.Value;
            }
            else
            {
                panel.Borders = BorderSide.None;
            }

            band.Controls.Add(panel);
            return panel;
        }

        /// <summary>
        /// A border-only rectangle (no fill) drawn directly on the band. Because it has
        /// no background it can never paint over the text placed inside it — unlike a
        /// filled panel, which XtraReports composites on top of overlapping siblings.
        /// </summary>
        private void Frame(Band band, float x, float y, float w, float h)
        {
            band.Controls.Add(new XRLabel
            {
                Text = string.Empty,
                LocationFloat = new PointFloat(x, y),
                SizeF = new SizeF(w, h),
                BackColor = Color.Transparent,
                Borders = BorderSide.All,
                BorderColor = LineColor
            });
        }

        /// <summary>A stacked caption + value field, added as a child of the employee panel.</summary>
        private void PanelField(XRControl panel, string caption, string value, float x, float y, float w)
        {
            Txt(panel, caption.ToUpper(Culture), x, y, w, 12, 7.5f, FontStyle.Bold, LeadAlign, CaptionMuted);
            Txt(panel, string.IsNullOrWhiteSpace(value) ? "—" : value, x, y + 13, w, 18, 10.5f, FontStyle.Bold, LeadAlign, TextDark);
        }

        /// <summary>A caption + value line, added as a child of the details panel.</summary>
        private void InfoLine(XRControl panel, string caption, string value, float y)
        {
            Txt(panel, caption, 18, y, 150, 16, 9f, FontStyle.Regular, LeadAlign, TextMuted);
            Txt(panel, string.IsNullOrWhiteSpace(value) ? "—" : value, 168, y, 214, 16, 9.5f, FontStyle.Bold, LeadAlign, TextDark);
        }

        /// <summary>A caption + amount line drawn on the band inside the summary frame (x 424..760).</summary>
        private float SummaryRow(XRControl band, string caption, decimal value, float y, Color valueColor, bool strong)
        {
            FontStyle style = strong ? FontStyle.Bold : FontStyle.Regular;
            Txt(band, caption, 442, y, 168, 18, 9.5f, style, LeadAlign, TextMuted);
            Txt(band, value.ToString("N2", Culture), 612, y, 130, 18, 9.5f, FontStyle.Bold, TrailAlign, valueColor);
            return y + 20;
        }

        private void SignatureSlot(Band band, string caption, float x)
        {
            const float w = 210;
            XRLabel rule = Txt(band, string.Empty, x, 334, w, 2, 8f, FontStyle.Regular, TextAlignment.MiddleCenter, TextMuted);
            rule.Borders = BorderSide.Bottom;
            rule.BorderColor = LineColor;
            Txt(band, caption, x, 340, w, 26, 8.5f, FontStyle.Bold, TextAlignment.MiddleCenter, TextMuted);
        }

        private void HeaderCell(Band band, string text, float x, float y, float w, TextAlignment align)
        {
            XRLabel cell = Txt(band, text, x, y, w, 26, 9f, FontStyle.Bold, align, Color.White);
            cell.BackColor = BrandGreenDark;
            cell.Padding = new PaddingInfo(8, 8, 3, 3);
        }

        private XRLabel BoundCell(Band band, string field, float x, float w, TextAlignment align, Color fore)
        {
            // Txt() already adds the label to the band — do not add it a second time.
            XRLabel cell = Txt(band, string.Empty, x, 0, w, 23, 9f, FontStyle.Regular, align, fore);
            cell.Borders = BorderSide.Bottom;
            cell.BorderColor = LineColor;
            cell.Padding = new PaddingInfo(8, 8, 2, 2);
            cell.DataBindings.Add("Text", null, field);
            return cell;
        }

        // The parent is a Band OR an XRPanel — both are XRControls. Labels that must sit
        // ON a coloured/bordered panel are added as CHILDREN of that panel (XtraReports
        // paints a filled panel over any siblings behind it, so a sibling label would be
        // hidden — the container relationship is what guarantees the text is on top).
        private XRLabel Txt(XRControl parent, string text, float x, float y, float w, float h,
            float size, FontStyle style, TextAlignment align, Color fore)
        {
            var label = new XRLabel
            {
                Text = text,
                LocationFloat = new PointFloat(x, y),
                SizeF = new SizeF(w, h),
                Font = new Font(_fontName, size, style),
                TextAlignment = align,
                ForeColor = fore,
                BackColor = Color.Transparent
            };
            parent.Controls.Add(label);
            return label;
        }

        // ======================================================================
        //  Identity / formatting helpers (display only)
        // ======================================================================

        private static CultureInfo Culture => CultureInfo.GetCultureInfo("fr-FR");

        private CultureInfo PeriodCulture => _isArabic ? CultureInfo.GetCultureInfo("ar-DZ") : Culture;

        private string CompanyName()
        {
            if (_model.Company == null) return string.Empty;
            return _isArabic && !string.IsNullOrWhiteSpace(_model.Company.NameAr) ? _model.Company.NameAr : _model.Company.NameFr;
        }

        private string CompanyAddress()
        {
            if (_model.Company == null) return string.Empty;
            return _isArabic && !string.IsNullOrWhiteSpace(_model.Company.AddressAr) ? _model.Company.AddressAr : _model.Company.AddressFr;
        }

        private string CompanyLegalLine()
        {
            Company c = _model.Company;
            if (c == null) return string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(c.Nif)) parts.Add(T("Company_Nif") + ": " + c.Nif);
            if (!string.IsNullOrWhiteSpace(c.Rc)) parts.Add(T("Company_Rc") + ": " + c.Rc);
            if (!string.IsNullOrWhiteSpace(c.CnasEmployerNumber)) parts.Add(T("Company_CnasEmployerNumber") + ": " + c.CnasEmployerNumber);
            return string.Join("     ", parts);
        }

        private string EmployeeName()
        {
            Employee e = _model.Employee;
            if (e == null) return string.Empty;
            return _isArabic && !string.IsNullOrWhiteSpace(e.LastNameAr)
                ? (e.LastNameAr + " " + e.FirstNameAr).Trim()
                : (e.LastNameFr + " " + e.FirstNameFr).Trim();
        }

        /// <summary>Matricule = the employee's existing identifier (no invented field).</summary>
        private string MatriculeText()
        {
            Employee e = _model.Employee;
            return (e == null || e.Id <= 0) ? null : e.Id.ToString("0000", Culture);
        }

        private string HireDate()
        {
            Employee e = _model.Employee;
            if (e == null || e.HireDate == default(DateTime)) return null;
            return e.HireDate.ToString("dd/MM/yyyy", Culture);
        }

        private string WorkedDaysText()
        {
            return _model.Payslip != null ? _model.Payslip.WorkedDays.ToString("0.##", Culture) : null;
        }

        private string PeriodLabel()
        {
            int m = _model.PeriodMonth;
            if (m < 1 || m > 12)
            {
                return m.ToString("00", CultureInfo.InvariantCulture) + "/" + _model.PeriodYear;
            }

            string month = PeriodCulture.DateTimeFormat.GetMonthName(m);
            if (!_isArabic && month.Length > 0)
            {
                month = char.ToUpper(month[0], Culture) + month.Substring(1);
            }

            return month + " " + _model.PeriodYear;
        }

        // ======================================================================
        //  Net amount in words (French)
        // ======================================================================

        private string AmountInWords(decimal net)
        {
            if (_isArabic)
            {
                return string.Empty; // words composed in French only
            }

            long dinars = (long)decimal.Truncate(net);
            int centimes = (int)decimal.Round((net - dinars) * 100m);
            if (dinars > 999999999L)
            {
                return string.Empty;
            }

            string words = FrenchWords(dinars);
            string text = char.ToUpper(words[0], Culture) + words.Substring(1) + " " + T("Payslip_Dinars");
            if (centimes > 0)
            {
                text += " et " + centimes.ToString("00", Culture) + " centimes";
            }

            return text;
        }

        private static readonly string[] Small =
        {
            "zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf",
            "dix", "onze", "douze", "treize", "quatorze", "quinze", "seize"
        };

        private static string Below100(int n)
        {
            if (n < 17) return Small[n];
            if (n < 20) return "dix-" + Small[n - 10];

            int t = n / 10;
            int u = n % 10;
            switch (t)
            {
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                    string b = new[] { "vingt", "trente", "quarante", "cinquante", "soixante" }[t - 2];
                    if (u == 0) return b;
                    if (u == 1) return b + " et un";
                    return b + "-" + Small[u];
                case 7:
                    if (u == 1) return "soixante et onze";
                    return "soixante-" + Below100(10 + u);
                case 8:
                    if (u == 0) return "quatre-vingts";
                    return "quatre-vingt-" + Small[u];
                default: // 9
                    return "quatre-vingt-" + Below100(10 + u);
            }
        }

        private static string Below1000(int n)
        {
            if (n < 100) return Below100(n);

            int h = n / 100;
            int r = n % 100;
            string hundred = h == 1 ? "cent" : Small[h] + " cent";
            if (r == 0) return h > 1 ? hundred + "s" : hundred;
            return hundred + " " + Below100(r);
        }

        private static string FrenchWords(long n)
        {
            if (n == 0) return Small[0];

            string result = string.Empty;
            int millions = (int)(n / 1000000L);
            int thousands = (int)((n % 1000000L) / 1000L);
            int rest = (int)(n % 1000L);

            if (millions > 0)
            {
                result += millions == 1 ? "un million" : Below1000(millions) + " millions";
            }

            if (thousands > 0)
            {
                if (result.Length > 0) result += " ";
                result += thousands == 1 ? "mille" : Below1000(thousands) + " mille";
            }

            if (rest > 0)
            {
                if (result.Length > 0) result += " ";
                result += Below1000(rest);
            }

            return result;
        }

        private sealed class PayslipLine
        {
            public string Code { get; set; }
            public string Designation { get; set; }
            public string Base { get; set; }
            public string Taux { get; set; }
            public string Gain { get; set; }
            public string Retenue { get; set; }
        }
    }
}
