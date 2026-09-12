using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Licensing;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Company-wide payroll run. Pure orchestration: it assembles, per employee, the SAME
    /// request the single-employee worksheet builds and calls the SAME
    /// <see cref="IPayrollService.Generate"/> in a loop. No engine logic lives here.
    /// <para>
    /// The request assembly in <see cref="BuildRequest"/> is a deliberate, field-for-field
    /// mirror of <c>PayrollViewModel.LoadWorksheet</c> + <c>PayrollViewModel.BuildRequest</c>
    /// (base salary → BaseSalaryOverride; active assigned elements; automatic loan deduction;
    /// attendance worked-days). Any divergence is a bug here and is caught by the
    /// batch-equals-single verification test.
    /// </para>
    /// </summary>
    public sealed class BatchPayrollService : IBatchPayrollService
    {
        private const string LoanLineLabel = "Remboursement prêt";

        private readonly IEmployeeService _employees;
        private readonly IPayrollElementService _elements;
        private readonly ILoanService _loans;
        private readonly IAttendanceService _attendance;
        private readonly IPayrollService _payroll;
        private readonly IContractService _contracts;
        private readonly IArchiveService _archive;
        private readonly Func<string, bool> _isModuleEnabled;

        public BatchPayrollService(
            IEmployeeService employees,
            IPayrollElementService elements,
            ILoanService loans,
            IAttendanceService attendance,
            IPayrollService payroll,
            IContractService contracts,
            IArchiveService archive,
            Func<string, bool> isModuleEnabled)
        {
            _employees = Guard.AgainstNull(employees, nameof(employees));
            _elements = Guard.AgainstNull(elements, nameof(elements));
            _loans = Guard.AgainstNull(loans, nameof(loans));
            _attendance = Guard.AgainstNull(attendance, nameof(attendance));
            _payroll = Guard.AgainstNull(payroll, nameof(payroll));
            _contracts = Guard.AgainstNull(contracts, nameof(contracts));
            _archive = Guard.AgainstNull(archive, nameof(archive));
            _isModuleEnabled = isModuleEnabled ?? (_ => true);
        }

        // ------------------------------------------------------------------ plan

        public BatchPayrollPlan Plan(long companyId, int year, int month)
        {
            DateTime firstOfMonth = new DateTime(year, month, 1);
            DateTime endOfMonth = firstOfMonth.AddMonths(1).AddDays(-1);

            var checks = new List<BatchEmployeeCheck>();
            // Date-aware population: an employee is on this run if they were employed AT ANY POINT in
            // the month — including a departing employee for their LAST month (exit date within the
            // month). A résiliation that takes effect on the 30th no longer silently drops the whole
            // month's salary. Employees who left before the month, or are disabled without an exit
            // date, are excluded. (Termination itself is unchanged; eligibility is driven by dates.)
            foreach (Employee emp in _employees.GetByCompany(companyId, true)
                         .Where(e => EmployedInPeriod(e, firstOfMonth, endOfMonth))
                         .OrderBy(e => e.LastNameFr))
            {
                var check = new BatchEmployeeCheck { EmployeeId = emp.Id, EmployeeName = Name(emp) };

                if (emp.BaseSalary <= 0m)
                {
                    check.Severity = BatchCheckSeverity.Blocking;
                    check.Reason = "Salaire de base manquant ou nul";
                }
                else if (!HadContractInPeriod(_contracts.GetByEmployee(emp.Id), firstOfMonth, endOfMonth))
                {
                    check.Severity = BatchCheckSeverity.Blocking;
                    check.Reason = "Aucun contrat couvrant ce mois";
                }
                else if (_isModuleEnabled(ModuleKeys.Attendance) && !HasAttendance(emp.Id, year, month))
                {
                    check.Severity = BatchCheckSeverity.Warning;
                    check.Reason = "Aucune présence saisie — mois complet supposé";
                }
                else
                {
                    check.Severity = BatchCheckSeverity.Ok;
                }

                checks.Add(check);
            }

            return new BatchPayrollPlan
            {
                CompanyId = companyId,
                Year = year,
                Month = month,
                AlreadyArchived = IsPeriodArchived(companyId, year, month),
                Employees = checks
            };
        }

        // ------------------------------------------------------------------ run

        public BatchPayrollResult Run(long companyId, int year, int month, IProgress<BatchProgress> progress = null)
        {
            BatchPayrollPlan plan = Plan(companyId, year, month);
            var results = new List<BatchEmployeeResult>();
            int total = plan.Employees.Count;
            int done = 0;

            foreach (BatchEmployeeCheck check in plan.Employees)
            {
                progress?.Report(new BatchProgress { Done = done, Total = total, CurrentEmployee = check.EmployeeName });

                var r = new BatchEmployeeResult { EmployeeId = check.EmployeeId, EmployeeName = check.EmployeeName };

                if (check.Severity == BatchCheckSeverity.Blocking)
                {
                    // Never silently produce a wrong payslip — skip and report.
                    r.Outcome = BatchOutcome.Skipped;
                    r.Message = check.Reason;
                }
                else
                {
                    PayrollGenerationRequest request = BuildRequest(companyId, check.EmployeeId, year, month);
                    Result<long> generated = _payroll.Generate(request);

                    if (generated.IsSuccess)
                    {
                        r.Outcome = BatchOutcome.Succeeded;
                        r.PayslipId = generated.Value;
                        r.Net = NetOf(generated.Value);
                        r.Message = check.Severity == BatchCheckSeverity.Warning ? check.Reason : string.Empty;

                        // Record the loan recovery exactly as the single-employee Save does: credit the
                        // loan the ENGINE-ROUNDED amount actually withheld on the archived fiche, not the
                        // theoretical instalment (keeps batch == single; only READS the computed amount).
                        if (_isModuleEnabled(ModuleKeys.Loans))
                        {
                            Payslip archived = _archive.GetPayslip(generated.Value);
                            decimal withheld = archived == null ? 0m : archived.Details
                                .Where(d => d.ElementType == ElementType.Deduction &&
                                            string.Equals(d.LabelFr, LoanLineLabel, StringComparison.Ordinal))
                                .Sum(d => d.Amount);
                            _loans.RecordPayrollDeductions(check.EmployeeId, year, month, withheld);
                        }
                    }
                    else
                    {
                        r.Outcome = BatchOutcome.Failed;
                        r.Message = generated.Error;
                    }
                }

                results.Add(r);
                done++;
            }

            progress?.Report(new BatchProgress { Done = done, Total = total, CurrentEmployee = string.Empty });

            return new BatchPayrollResult
            {
                CompanyId = companyId,
                Year = year,
                Month = month,
                RunId = RunIdFor(companyId, year, month),
                Results = results
            };
        }

        // ------------------------------------------------------- request assembly

        public PayrollGenerationRequest BuildRequest(long companyId, long employeeId, int year, int month)
        {
            Employee employee = _employees.Get(employeeId);
            if (employee == null)
            {
                return null;
            }

            decimal monthDays = DateTime.DaysInMonth(year, month);
            var elements = new List<PayrollElementEntry>();

            // Active assigned elements — mirrors PayrollViewModel.LoadWorksheet.
            foreach (EmployeeElement assignment in _employees.GetElements(employeeId))
            {
                if (!assignment.IsActive)
                {
                    continue;
                }

                PayrollElement el = _elements.Get(assignment.ElementId);
                if (el == null || el.IsDeleted || !el.IsEnabled)
                {
                    continue;
                }

                bool isGain = el.ElementType == ElementType.Gain;
                elements.Add(new PayrollElementEntry
                {
                    ElementId = el.Id,
                    LineAmount = assignment.Amount ?? el.DefaultAmount ?? 0m,
                    IsManual = false,
                    ManualLabel = el.NameFr,
                    ManualType = isGain ? ElementType.Gain : ElementType.Deduction
                });
            }

            // Automatic loan deduction — mirrors PayrollViewModel.LoadWorksheet.
            if (_isModuleEnabled(ModuleKeys.Loans))
            {
                decimal loanDue = _loans.GetMonthlyDeduction(employeeId, year, month);
                if (loanDue > 0m)
                {
                    elements.Add(new PayrollElementEntry
                    {
                        ElementId = 0,
                        LineAmount = loanDue,
                        IsManual = true,
                        ManualLabel = LoanLineLabel,
                        ManualType = ElementType.Deduction
                    });
                }
            }

            // Automatic attendance worked-days — mirrors PayrollViewModel.BuildRequest.
            decimal workedDays = monthDays;
            decimal workedHours = 0m;
            if (_isModuleEnabled(ModuleKeys.Attendance))
            {
                AttendanceSummary summary = _attendance.GetMonthlySummary(employeeId, year, month);
                if (summary != null && summary.RecordedDays > 0)
                {
                    workedDays = Math.Max(0m, monthDays - summary.AbsentDays);
                    workedHours = summary.WorkedHours;
                }
            }

            return new PayrollGenerationRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                Year = year,
                Month = month,
                WorkedDays = workedDays,
                WorkableDays = monthDays,
                WorkedHours = workedHours,
                BaseSalaryOverride = employee.BaseSalary,
                Elements = elements
            };
        }

        // ------------------------------------------------------------------ helpers

        private bool HasAttendance(long employeeId, int year, int month)
        {
            AttendanceSummary s = _attendance.GetMonthlySummary(employeeId, year, month);
            return s != null && s.RecordedDays > 0;
        }

        private bool IsPeriodArchived(long companyId, int year, int month)
        {
            PayrollRun run = _archive.SearchRuns(companyId, year, month).FirstOrDefault();
            return run != null && run.RunStatus == RunStatus.Archived;
        }

        private long RunIdFor(long companyId, int year, int month)
        {
            PayrollRun run = _archive.SearchRuns(companyId, year, month).FirstOrDefault();
            return run != null ? run.Id : 0L;
        }

        private decimal NetOf(long payslipId)
        {
            Payslip p = _archive.GetPayslip(payslipId);
            return p != null ? p.NetSalaire : 0m;
        }

        private static string Name(Employee e) => (e.LastNameFr + " " + e.FirstNameFr).Trim();

        /// <summary>True if the employee was employed at any point during [start, end]: hired on or
        /// before the month end, and either still employed (no exit date, and active) or their exit
        /// date falls on or after the month start (so their final month is still paid).</summary>
        private static bool EmployedInPeriod(Employee e, DateTime start, DateTime end)
        {
            if (e.HireDate.Date > end) return false;                       // not yet hired this month
            if (e.ExitDate.HasValue) return e.ExitDate.Value.Date >= start; // paid up to and incl. exit month
            return e.IsActive;                                             // no exit → only if active
        }

        /// <summary>True if the employee had a real (non-draft) contract in force at any point during
        /// [start, end] — including a contract terminated within the month (its end date on/after the
        /// month start). Drives the batch's "a contract covers this month" gate by dates, not by the
        /// contract's current stored status.</summary>
        private static bool HadContractInPeriod(IEnumerable<OptiPaie.Core.Dtos.ContractSummary> contracts, DateTime start, DateTime end)
        {
            return contracts.Any(c =>
                c.Status != ContractStatus.Draft &&
                c.StartDate.Date <= end &&
                (!c.EndDate.HasValue || c.EndDate.Value.Date >= start));
        }
    }
}
