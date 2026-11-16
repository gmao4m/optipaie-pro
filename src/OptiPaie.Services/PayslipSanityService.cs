using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Payroll;

namespace OptiPaie.Services
{
    /// <summary>
    /// The pre-issue sanity guard (PART 2). Pure: it reads the engine's own
    /// <see cref="PayrollResult"/> and returns findings. It changes no number, takes no
    /// dependency, and never touches the payroll engine. A payslip with any finding must
    /// not be issued without a deliberate, audited confirmation — enforced in
    /// <see cref="PayrollService"/>.
    /// <para>
    /// Note on rule 4 (large exempt gain): the owner's rule is "exempt from BOTH bases
    /// while a recurring monthly element". A payslip line does not carry periodicity, and
    /// flagging EVERY small exempt allowance (panier, transport) would force a
    /// confirmation on ordinary payslips and train users to click through. So rule 4 fires
    /// on a gain that is exempt from both bases AND is a material share of the gross
    /// (&gt;= 50%) — the "a salary is hiding in an exempt line" pattern (payslip #22), not
    /// a legitimate small allowance. Every exempt element, of any size, is still listed in
    /// the PART 3 exposure diagnostic, so nothing is hidden.
    /// </para>
    /// </summary>
    public sealed class PayslipSanityService : IPayslipSanityService
    {
        private const decimal LargeExemptShareOfGross = 0.50m;

        public PayslipSanityReport Inspect(PayrollResult result, decimal snmg, bool fullTime)
        {
            var warnings = new List<PayslipSanityWarning>();
            if (result == null || result.Totals == null)
            {
                return PayslipSanityReport.Clean;
            }

            PayrollTotals t = result.Totals;
            decimal gross = t.SalaireBrut;

            // Rule 1 — a paid salary with no employee contribution.
            if (gross > 0m && t.CnasEmployee == 0m)
            {
                warnings.Add(new PayslipSanityWarning(
                    PayslipSanityCode.ContributionZeroOnPaidSalary,
                    "Salaire brut de " + Money(gross) + " DA mais cotisation CNAS salariale nulle — "
                        + "vérifiez que les éléments de rémunération ne sont pas tous exonérés de cotisation.",
                    "أجر خام " + Money(gross) + " دج لكن اشتراك CNAS للأجير معدوم — "
                        + "تحقّق من أنّ عناصر الأجر ليست كلّها معفاة من الاشتراك."));
            }

            // Rule 2 — net equals gross exactly (nothing withheld).
            if (gross > 0m && t.NetSalaire == gross)
            {
                warnings.Add(new PayslipSanityWarning(
                    PayslipSanityCode.NetEqualsGross,
                    "Le net à payer (" + Money(t.NetSalaire) + " DA) est exactement égal au brut : "
                        + "aucune retenue n'a été appliquée.",
                    "الصافي المستحقّ (" + Money(t.NetSalaire) + " دج) يساوي تماماً الأجر الخام: "
                        + "لم تُطبَّق أيّ اقتطاعات."));
            }

            // Rule 3 — two applied (non-zero) lines share a name.
            IReadOnlyList<PayrollLineResult> lines = result.Lines ?? new List<PayrollLineResult>();
            foreach (IGrouping<string, PayrollLineResult> group in lines
                .Where(l => l.Amount != 0m && !string.IsNullOrWhiteSpace(l.LabelFr))
                .GroupBy(l => l.LabelFr.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1))
            {
                string name = group.First().LabelFr.Trim();
                warnings.Add(new PayslipSanityWarning(
                    PayslipSanityCode.DuplicateLineName,
                    "Deux lignes actives portent le même nom « " + name + " » — un doublon fausse la base. "
                        + "Renommez ou supprimez-en une.",
                    "سطران فعّالان يحملان نفس الاسم « " + name + " » — التكرار يُفسد الوعاء. "
                        + "أعِد التسمية أو احذف أحدهما.",
                    name));
            }

            // Rule 4 — a large gain exempt from BOTH bases (the "hidden salary" pattern).
            if (gross > 0m)
            {
                foreach (PayrollLineResult line in lines.Where(l =>
                    l.ElementType == ElementType.Gain &&
                    !l.IsCnasApplicable && !l.IsIrgApplicable &&
                    l.Amount > 0m &&
                    l.Amount >= LargeExemptShareOfGross * gross))
                {
                    int pct = (int)Math.Round(line.Amount / gross * 100m, MidpointRounding.AwayFromZero);
                    string name = string.IsNullOrWhiteSpace(line.LabelFr) ? "(sans nom)" : line.LabelFr.Trim();
                    warnings.Add(new PayslipSanityWarning(
                        PayslipSanityCode.LargeExemptGain,
                        "L'élément « " + name + " » (" + Money(line.Amount) + " DA, " + pct + "% du brut) est exonéré "
                            + "à la fois de la base cotisable et de la base imposable — une part aussi importante hors assiette est inhabituelle.",
                        "العنصر « " + name + " » (" + Money(line.Amount) + " دج، " + pct + "٪ من الخام) معفى "
                            + "من الوعاءين معاً — إعفاء حصّة بهذا الحجم غير معتاد.",
                        name));
                }
            }

            // Rule 5 — contribution base below the SNMG for a full-time month.
            if (fullTime && snmg > 0m && t.BaseCotisable > 0m && t.BaseCotisable < snmg)
            {
                warnings.Add(new PayslipSanityWarning(
                    PayslipSanityCode.BelowSnmgFullTime,
                    "Base cotisable (" + Money(t.BaseCotisable) + " DA) inférieure au SNMG (" + Money(snmg) + " DA) "
                        + "pour un mois complet — un temps plein ne peut être payé sous le SNMG.",
                    "الوعاء الخاضع للاشتراك (" + Money(t.BaseCotisable) + " دج) أقلّ من الأجر الأدنى SNMG (" + Money(snmg) + " دج) "
                        + "لشهر كامل — لا يجوز دفع أجر دون SNMG لعامل بدوام كامل."));
            }

            return new PayslipSanityReport(warnings);
        }

        private static string Money(decimal value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
