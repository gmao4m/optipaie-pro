using System;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;

namespace OptiPaie.Services
{
    /// <summary>
    /// The single mapping from an engine <see cref="PayrollResult"/> to the persisted
    /// <see cref="Payslip"/> and its <see cref="PayrollDetail"/> lines. Both the normal
    /// generation path (<see cref="PayrollService"/>) and the correction path
    /// (<see cref="PayslipRevisionService"/>) go through here, so a corrected version is
    /// built byte-for-byte the same way as a freshly generated one — there is no second,
    /// drifting copy of the mapping. It contains no calculation.
    /// </summary>
    internal static class PayslipFactory
    {
        public static Payslip BuildPayslip(
            long runId,
            long employeeId,
            PayrollResult result,
            decimal cnasEmployeeRate,
            decimal cnasEmployerRate,
            decimal workedDays,
            decimal workedHours,
            PayslipStatus status = PayslipStatus.Active,
            long? supersedesPayslipId = null)
        {
            PayrollTotals totals = result.Totals;

            return new Payslip
            {
                RunId = runId,
                EmployeeId = employeeId,
                SalaireBrut = totals.SalaireBrut,
                BaseCotisable = totals.BaseCotisable,
                CnasEmployee = totals.CnasEmployee,
                CnasEmployer = totals.CnasEmployer,
                BaseImposable = totals.BaseImposable,
                IrgBrut = totals.IrgBrut,
                Abattement = totals.Abattement,
                Irg = totals.Irg,
                NetSalaire = totals.NetSalaire,
                CnasEmployeeRateUsed = cnasEmployeeRate,
                CnasEmployerRateUsed = cnasEmployerRate,
                WorkedDays = workedDays,
                WorkedHours = workedHours,
                EngineVersion = result.EngineVersion,
                GeneratedAtUtc = DateTime.UtcNow,
                Status = status,
                SupersedesPayslipId = supersedesPayslipId
            };
        }

        public static PayrollDetail BuildDetail(long payslipId, PayrollLineResult line)
        {
            return new PayrollDetail
            {
                PayslipId = payslipId,
                // A free (manual) worksheet line — e.g. the automatic "Remboursement prêt" — has no
                // catalog element, so it carries id 0. Store that as NULL: the archive column has a
                // foreign key to PayrollElements and 0 is not a real element. Reference only.
                ElementId = line.ElementId.HasValue && line.ElementId.Value != 0 ? line.ElementId : (long?)null,
                LabelFr = line.LabelFr,
                LabelAr = line.LabelAr,
                ElementType = line.ElementType,
                Base = line.Base,
                Rate = line.Rate,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Amount = line.Amount,
                IsCnasApplicable = line.IsCnasApplicable,
                IsIrgApplicable = line.IsIrgApplicable,
                DisplayOrder = line.DisplayOrder
            };
        }
    }
}
