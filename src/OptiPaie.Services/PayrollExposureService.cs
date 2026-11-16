using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Payroll;

namespace OptiPaie.Services
{
    /// <summary>
    /// PART 3 — the read-only exposure diagnostic. Lists, from the active element catalog,
    /// every gain exempt from a base (with no cap) and every element carrying an exemption
    /// ceiling that the engine does not enforce. Pure read; changes nothing; never touches
    /// the engine.
    /// </summary>
    public sealed class PayrollExposureService : IPayrollExposureService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public PayrollExposureService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        public PayrollExposureReport BuildCatalogExposure()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                List<PayrollElement> active = uow.PayrollElements
                    .GetAll(includeDisabled: false, includeDeleted: false)
                    .Where(e => e.IsEnabled && !e.IsDeleted)
                    .ToList();

                var exempt = new List<ExemptElementInfo>();
                foreach (PayrollElement e in active
                    .Where(e => e.ElementType == ElementType.Gain && (!e.IsCnasApplicable || !e.IsIrgApplicable))
                    .OrderBy(e => e.NameFr, System.StringComparer.CurrentCultureIgnoreCase))
                {
                    bool exContrib = !e.IsCnasApplicable;
                    bool exTax = !e.IsIrgApplicable;
                    decimal? fixedAmount = e.CalculationMethod == CalculationMethod.FixedAmount ? e.DefaultAmount : null;

                    string fr, ar;
                    if (exContrib && exTax)
                    {
                        fr = "Exonéré des deux assiettes (cotisable ET imposable). Aucun plafond n'est appliqué.";
                        ar = "معفى من الوعاءين (الاشتراك والضريبة). لا يُطبَّق أيّ سقف.";
                    }
                    else if (exContrib)
                    {
                        fr = "Hors base cotisable (CNAS). Aucun plafond n'est appliqué.";
                        ar = "خارج وعاء الاشتراك (CNAS). لا يُطبَّق أيّ سقف.";
                    }
                    else
                    {
                        fr = "Hors base imposable (IRG). Aucun plafond n'est appliqué.";
                        ar = "خارج الوعاء الخاضع للضريبة (IRG). لا يُطبَّق أيّ سقف.";
                    }

                    exempt.Add(new ExemptElementInfo(e.NameFr, exContrib, exTax, fixedAmount, fr, ar));
                }

                var ceilings = new List<CeilingElementInfo>();
                foreach (PayrollElement e in active
                    .Where(e => e.ExemptionCeiling.HasValue)
                    .OrderBy(e => e.NameFr, System.StringComparer.CurrentCultureIgnoreCase))
                {
                    string c = e.ExemptionCeiling.Value.ToString("0.##", CultureInfo.InvariantCulture);
                    ceilings.Add(new CeilingElementInfo(
                        e.NameFr,
                        e.ExemptionCeiling.Value,
                        "Plafond d'exonération défini (" + c + " DA) mais NON appliqué par le moteur actuellement : "
                            + "la part au-dessus du plafond n'est pas remise dans l'assiette.",
                        "سقف إعفاء محدَّد (" + c + " دج) لكنّه غير مطبَّق حالياً من المحرّك: "
                            + "الجزء فوق السقف لا يُعاد إلى الوعاء."));
                }

                return new PayrollExposureReport(exempt, ceilings);
            }
        }
    }
}
