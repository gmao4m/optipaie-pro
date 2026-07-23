using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Constants;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Engine;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.PayrollEngine;

namespace OptiPaie.Services
{
    /// <summary>
    /// Orchestrates payroll generation: builds the engine context from stored
    /// employee/element/configuration data, runs the pure engine and persists the
    /// run, payslip and details in a single transaction. It contains no payroll
    /// calculation — all maths live in the engine.
    /// </summary>
    public sealed class PayrollService : IPayrollService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly IConfigurationService _configurationService;
        private readonly IPayrollEngine _engine;

        public PayrollService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IConfigurationService configurationService,
            IPayrollEngine engine)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _configurationService = Guard.AgainstNull(configurationService, nameof(configurationService));
            _engine = Guard.AgainstNull(engine, nameof(engine));
        }

        public PayrollResult Preview(PayrollGenerationRequest request)
        {
            BuildOutcome outcome = BuildContext(request);
            return outcome.Failure ?? _engine.Calculate(outcome.Context);
        }

        public Result<long> Generate(PayrollGenerationRequest request)
        {
            BuildOutcome outcome = BuildContext(request);
            if (outcome.Failure != null)
            {
                return ToFailure(outcome.Failure);
            }

            PayrollResult result = _engine.Calculate(outcome.Context);
            if (!result.IsSuccess)
            {
                return ToFailure(result);
            }

            return Persist(request, outcome, result);
        }

        // -- Context building & pre-checks ---------------------------------------

        private BuildOutcome BuildContext(PayrollGenerationRequest request)
        {
            if (request == null)
            {
                return BuildOutcome.Fail(PayrollErrorCodes.RequestMissing, "La requête de paie est absente.");
            }

            if (!TryCreatePeriod(request.Year, request.Month, out PayrollPeriod period))
            {
                return BuildOutcome.Fail(PayrollErrorCodes.PeriodInvalid, "La période de paie est invalide.");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Employee employee = uow.Employees.GetById(request.EmployeeId);
                if (employee == null || employee.IsDeleted)
                {
                    return BuildOutcome.Fail(PayrollErrorCodes.EmployeeNotFound, "Employé introuvable.");
                }

                if (employee.CompanyId != request.CompanyId || !uow.Companies.ExistsById(request.CompanyId))
                {
                    return BuildOutcome.Fail(PayrollErrorCodes.CompanyNotFound, "Entreprise introuvable.");
                }

                if (period > PayrollPeriod.FromDate(DateTime.Today))
                {
                    return BuildOutcome.Fail(PayrollErrorCodes.PeriodFuture, "Impossible de générer une paie pour une période future.");
                }

                if (!IsActiveInPeriod(employee, period))
                {
                    return BuildOutcome.Fail(PayrollErrorCodes.EmployeeNotActive, "L'employé n'était pas en activité durant cette période.");
                }

                LegalSnapshot snapshot = _configurationService.GetLegalSnapshot();
                ResolveWorkedDays(request, employee, period, out decimal workedDays, out decimal workableDays);
                IList<PayrollElementInput> elements = ResolveElements(request, uow);

                // The worksheet may override the base salary for this month; null keeps
                // the stored value. This is an input only — no formula/rate changes.
                decimal baseSalary = request.BaseSalaryOverride.HasValue && request.BaseSalaryOverride.Value >= 0m
                    ? request.BaseSalaryOverride.Value
                    : employee.BaseSalary;

                var context = new PayrollContext
                {
                    Period = period,
                    BaseSalary = baseSalary,
                    WorkedDays = workedDays,
                    WorkableDays = workableDays,
                    WorkedHours = request.WorkedHours,
                    Legal = snapshot,
                    Elements = elements
                };

                return BuildOutcome.Ok(context, snapshot);
            }
        }

        private static bool IsActiveInPeriod(Employee employee, PayrollPeriod period)
        {
            DateTime periodStart = period.FirstDay;
            DateTime periodEnd = period.LastDay;

            bool hiredBeforeEnd = employee.HireDate.Date <= periodEnd;
            bool notLeftBeforeStart = !employee.ExitDate.HasValue || employee.ExitDate.Value.Date >= periodStart;

            return hiredBeforeEnd && notLeftBeforeStart;
        }

        private static void ResolveWorkedDays(
            PayrollGenerationRequest request,
            Employee employee,
            PayrollPeriod period,
            out decimal workedDays,
            out decimal workableDays)
        {
            if (request.WorkableDays > 0m)
            {
                workableDays = request.WorkableDays;
                workedDays = request.WorkedDays;
                return;
            }

            workableDays = period.DaysInMonth;

            DateTime periodStart = period.FirstDay;
            DateTime periodEnd = period.LastDay;
            DateTime start = employee.HireDate.Date > periodStart ? employee.HireDate.Date : periodStart;
            DateTime end = employee.ExitDate.HasValue && employee.ExitDate.Value.Date < periodEnd
                ? employee.ExitDate.Value.Date
                : periodEnd;

            int days = (end - start).Days + 1;
            if (days < 0)
            {
                days = 0;
            }
            else if (days > period.DaysInMonth)
            {
                days = period.DaysInMonth;
            }

            workedDays = days;
        }

        private static IList<PayrollElementInput> ResolveElements(PayrollGenerationRequest request, IUnitOfWork uow)
        {
            IEnumerable<PayrollElementEntry> entries = request.Elements != null && request.Elements.Count > 0
                ? request.Elements
                : BuildEntriesFromAssignments(request.EmployeeId, uow);

            var inputs = new List<PayrollElementInput>();

            foreach (PayrollElementEntry entry in entries)
            {
                // Free (non-catalog) element created directly on the worksheet: a fixed
                // amount with the standard tax treatment for its nature.
                if (entry.IsManual)
                {
                    bool isGain = entry.ManualType == ElementType.Gain;
                    inputs.Add(new PayrollElementInput
                    {
                        ElementId = 0,
                        LabelFr = string.IsNullOrWhiteSpace(entry.ManualLabel) ? "Élément" : entry.ManualLabel,
                        LabelAr = string.IsNullOrWhiteSpace(entry.ManualLabel) ? "عنصر" : entry.ManualLabel,
                        ElementType = entry.ManualType,
                        CalculationMethod = CalculationMethod.FixedAmount,
                        Amount = entry.LineAmount ?? 0m,
                        IsCnasApplicable = isGain,
                        IsIrgApplicable = isGain,
                        CnasFactor = isGain ? 1m : 0m,
                        IrgFactor = isGain ? 1m : 0m,
                        IsIncludedInGross = isGain,
                        Periodicity = ElementPeriodicity.Monthly,
                        DisplayOrder = 0
                    });
                    continue;
                }

                PayrollElement element = uow.PayrollElements.GetById(entry.ElementId);
                if (element == null || element.IsDeleted || !element.IsEnabled)
                {
                    continue;
                }

                // Legal treatment read from the element: a partial percent overrides
                // the yes/no flag; null percent keeps the flag's 100 %/0 % behaviour.
                decimal cnasFactor = element.CnasPercent.HasValue
                    ? ClampFactor(element.CnasPercent.Value / 100m)
                    : (element.IsCnasApplicable ? 1m : 0m);
                decimal irgFactor = element.IrgPercent.HasValue
                    ? ClampFactor(element.IrgPercent.Value / 100m)
                    : (element.IsIrgApplicable ? 1m : 0m);

                var input = new PayrollElementInput
                {
                    ElementId = element.Id,
                    LabelFr = element.NameFr,
                    LabelAr = element.NameAr,
                    ElementType = element.ElementType,
                    // When the worksheet supplies the evaluated amount (Base × Taux),
                    // use it directly as a fixed amount — keeps the element's CNAS/IRG
                    // treatment while honouring exactly what the accountant sees.
                    CalculationMethod = entry.LineAmount.HasValue ? CalculationMethod.FixedAmount : element.CalculationMethod,
                    CalculationBase = element.CalculationBase,
                    Amount = entry.LineAmount ?? entry.Amount ?? element.DefaultAmount,
                    Rate = entry.Rate ?? element.DefaultRate,
                    Quantity = entry.Quantity ?? element.DefaultQuantity,
                    UnitPrice = entry.UnitPrice ?? element.DefaultUnitPrice,
                    IsCnasApplicable = cnasFactor > 0m,
                    IsIrgApplicable = irgFactor > 0m,
                    CnasFactor = cnasFactor,
                    IrgFactor = irgFactor,
                    IsIncludedInGross = element.IsIncludedInGross,
                    ExemptionCeiling = element.ExemptionCeiling,
                    Periodicity = element.Periodicity,
                    DisplayOrder = element.DisplayOrder
                };

                if (entry.LineAmount.HasValue == false && element.IncludedInLissage)
                {
                    int months = entry.LissageMonths ?? PeriodicityMonths(element.Periodicity);
                    IEnumerable<decimal> referenceBases = entry.LissageReferenceBases ?? Enumerable.Empty<decimal>();
                    input.Lissage = new LissageInput(months, referenceBases);
                }

                inputs.Add(input);
            }

            return inputs;
        }

        private static IEnumerable<PayrollElementEntry> BuildEntriesFromAssignments(long employeeId, IUnitOfWork uow)
        {
            foreach (EmployeeElement assignment in uow.EmployeeElements.GetByEmployee(employeeId, activeOnly: true))
            {
                yield return new PayrollElementEntry
                {
                    ElementId = assignment.ElementId,
                    Amount = assignment.Amount,
                    Rate = assignment.Rate,
                    Quantity = assignment.Quantity,
                    UnitPrice = assignment.UnitPrice
                };
            }
        }

        private static decimal ClampFactor(decimal value)
        {
            if (value < 0m) return 0m;
            if (value > 1m) return 1m;
            return value;
        }

        private static int PeriodicityMonths(ElementPeriodicity periodicity)
        {
            switch (periodicity)
            {
                case ElementPeriodicity.Quarterly:
                    return 3;
                case ElementPeriodicity.Annual:
                    return 12;
                default:
                    return 1;
            }
        }

        // -- Persistence ----------------------------------------------------------

        private Result<long> Persist(PayrollGenerationRequest request, BuildOutcome outcome, PayrollResult result)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                try
                {
                    PayrollRun run = uow.PayrollRuns.GetByCompanyAndPeriod(request.CompanyId, request.Year, request.Month);

                    if (run != null && run.RunStatus == RunStatus.Archived)
                    {
                        uow.Rollback();
                        return Result.Fail<long>("La paie de cette période est déjà archivée.", PayrollErrorCodes.DuplicatePayroll);
                    }

                    if (run == null)
                    {
                        run = new PayrollRun
                        {
                            CompanyId = request.CompanyId,
                            PeriodYear = request.Year,
                            PeriodMonth = request.Month,
                            RunStatus = RunStatus.Generated,
                            GeneratedAtUtc = DateTime.UtcNow,
                            EngineVersion = result.EngineVersion
                        };
                        uow.PayrollRuns.Insert(run);
                    }
                    else
                    {
                        bool alreadyHasPayslip = uow.Payslips.GetByRun(run.Id)
                            .Any(p => p.EmployeeId == request.EmployeeId);

                        if (alreadyHasPayslip)
                        {
                            uow.Rollback();
                            return Result.Fail<long>(
                                "Une paie existe déjà pour cet employé sur cette période.",
                                PayrollErrorCodes.DuplicatePayroll);
                        }

                        run.RunStatus = RunStatus.Generated;
                        run.GeneratedAtUtc = DateTime.UtcNow;
                        run.EngineVersion = result.EngineVersion;
                        uow.PayrollRuns.Update(run);
                    }

                    Payslip payslip = MapPayslip(request, outcome, result, run.Id);
                    uow.Payslips.Insert(payslip);

                    foreach (PayrollLineResult line in result.Lines)
                    {
                        uow.PayrollDetails.Insert(MapDetail(payslip.Id, line));
                    }

                    uow.Commit();
                    return Result.Ok(payslip.Id);
                }
                catch (Exception)
                {
                    uow.Rollback();
                    return Result.Fail<long>("Erreur lors de l'enregistrement de la paie.", PayrollErrorCodes.PersistFailed);
                }
            }
        }

        private static Payslip MapPayslip(
            PayrollGenerationRequest request,
            BuildOutcome outcome,
            PayrollResult result,
            long runId)
        {
            PayrollTotals totals = result.Totals;

            return new Payslip
            {
                RunId = runId,
                EmployeeId = request.EmployeeId,
                SalaireBrut = totals.SalaireBrut,
                BaseCotisable = totals.BaseCotisable,
                CnasEmployee = totals.CnasEmployee,
                CnasEmployer = totals.CnasEmployer,
                BaseImposable = totals.BaseImposable,
                IrgBrut = totals.IrgBrut,
                Abattement = totals.Abattement,
                Irg = totals.Irg,
                NetSalaire = totals.NetSalaire,
                CnasEmployeeRateUsed = outcome.Snapshot.CnasEmployeeRate,
                CnasEmployerRateUsed = outcome.Snapshot.CnasEmployerRate,
                WorkedDays = outcome.Context.WorkedDays,
                WorkedHours = outcome.Context.WorkedHours,
                EngineVersion = result.EngineVersion,
                GeneratedAtUtc = DateTime.UtcNow
            };
        }

        private static PayrollDetail MapDetail(long payslipId, PayrollLineResult line)
        {
            return new PayrollDetail
            {
                PayslipId = payslipId,
                // A free (manual) worksheet line — e.g. the automatic "Remboursement prêt" —
                // has no catalog element, so it carries id 0. Store that as NULL: the archive
                // column has a foreign key to PayrollElements and 0 is not a real element, so
                // a manual line would otherwise fail to persist. Reference only — no amount,
                // rate, base or any calculated value is affected.
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

        // -- Helpers --------------------------------------------------------------

        private static bool TryCreatePeriod(int year, int month, out PayrollPeriod period)
        {
            if (year >= 2000 && year <= 2100 && month >= 1 && month <= 12)
            {
                period = new PayrollPeriod(year, month);
                return true;
            }

            period = default(PayrollPeriod);
            return false;
        }

        private static Result<long> ToFailure(PayrollResult result)
        {
            PayrollMessage error = result.Errors.FirstOrDefault();
            return error != null
                ? Result.Fail<long>(error.Text, error.Code)
                : Result.Fail<long>("Échec du calcul de la paie.", PayrollErrorCodes.ContextMissing);
        }

        /// <summary>Internal carrier for the build step: either a failure or a ready context.</summary>
        private sealed class BuildOutcome
        {
            private BuildOutcome()
            {
            }

            public PayrollResult Failure { get; private set; }
            public PayrollContext Context { get; private set; }
            public LegalSnapshot Snapshot { get; private set; }

            public static BuildOutcome Ok(PayrollContext context, LegalSnapshot snapshot)
            {
                return new BuildOutcome { Context = context, Snapshot = snapshot };
            }

            public static BuildOutcome Fail(string code, string text)
            {
                var message = PayrollMessage.Error(code, text);
                return new BuildOutcome
                {
                    Failure = PayrollResult.Failed(
                        new[] { message },
                        EngineVersion.Version,
                        string.Empty,
                        EngineVersion.CalculationVersion)
                };
            }
        }
    }
}
