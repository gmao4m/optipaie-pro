using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Leave module orchestration. Owns ALL leave rules so every screen and the
    /// payroll chain agree:
    ///   • leave days exclude the Algerian rest days (Friday/Saturday);
    ///   • two live requests of one employee may never overlap;
    ///   • annual entitlement = 2,5 days per month worked, capped at 30 (loi 90-11);
    ///   • approving writes the days into Attendance, cancelling removes them.
    /// The last rule is the cross-module synchronisation: no import, no export, no
    /// duplicated day — payroll reads the same attendance rows as everything else.
    /// </summary>
    public sealed class LeaveService : ILeaveService
    {
        private const string KeyDaysPerMonth = "Leave.DaysPerMonth";
        private const string KeyAnnualCap = "Leave.AnnualCap";
        private const string KeyExcludeRest = "Leave.ExcludeRestDays";

        /// <summary>Marks the attendance rows this module owns, so it only removes its own.</summary>
        private const string AttendanceMarker = "[Congé]";

        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public LeaveService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        public Result<long> Save(LeaveRequest request)
        {
            if (request == null)
            {
                return Result.Fail<long>("Aucune demande de congé.", "Leave_Required");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                LeaveSettings settings = ReadSettings(uow);

                if (request.Id > 0)
                {
                    LeaveRequest existing = uow.Leave.GetById(request.Id);
                    if (existing == null)
                    {
                        return Result.Fail<long>("Demande introuvable.", "Leave_NotFound");
                    }

                    if (existing.Status != LeaveStatus.Pending)
                    {
                        return Result.Fail<long>(
                            "Seule une demande en attente peut être modifiée.", "Leave_NotEditable");
                    }

                    request.CreatedAtUtc = existing.CreatedAtUtc;
                    request.Status = LeaveStatus.Pending;
                }
                else
                {
                    request.Status = LeaveStatus.Pending;
                }

                Result validation = Validate(uow, request);
                if (validation.IsFailure)
                {
                    return Result.Fail<long>(validation.Error, validation.ErrorCode);
                }

                request.StartDate = request.StartDate.Date;
                request.EndDate = request.EndDate.Date;
                request.Days = Count(request.StartDate, request.EndDate, settings);

                if (request.Days <= 0m)
                {
                    return Result.Fail<long>(
                        "La période ne contient aucun jour de congé (jours de repos uniquement).",
                        "Leave_NoWorkingDay");
                }

                if (request.Id > 0)
                {
                    uow.Leave.Update(request);
                    return Result.Ok(request.Id);
                }

                return Result.Ok(uow.Leave.Insert(request));
            }
        }

        public Result Approve(long id, string note)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                LeaveRequest request = uow.Leave.GetById(id);
                if (request == null)
                {
                    return Result.Fail("Demande introuvable.", "Leave_NotFound");
                }

                if (request.Status == LeaveStatus.Approved)
                {
                    return Result.Ok();
                }

                if (request.Status != LeaveStatus.Pending)
                {
                    return Result.Fail("Seule une demande en attente peut être approuvée.", "Leave_NotPending");
                }

                LeaveSettings settings = ReadSettings(uow);

                uow.BeginTransaction();
                try
                {
                    request.Status = LeaveStatus.Approved;
                    request.DecisionNote = note;
                    request.DecidedAtUtc = DateTime.UtcNow;
                    uow.Leave.Update(request);

                    WriteAttendance(uow, request, settings);

                    uow.Commit();
                    return Result.Ok();
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public Result Reject(long id, string note)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                LeaveRequest request = uow.Leave.GetById(id);
                if (request == null)
                {
                    return Result.Fail("Demande introuvable.", "Leave_NotFound");
                }

                if (request.Status != LeaveStatus.Pending)
                {
                    return Result.Fail("Seule une demande en attente peut être refusée.", "Leave_NotPending");
                }

                request.Status = LeaveStatus.Rejected;
                request.DecisionNote = note;
                request.DecidedAtUtc = DateTime.UtcNow;
                uow.Leave.Update(request);
                return Result.Ok();
            }
        }

        public Result Cancel(long id, string note)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                LeaveRequest request = uow.Leave.GetById(id);
                if (request == null)
                {
                    return Result.Fail("Demande introuvable.", "Leave_NotFound");
                }

                if (request.Status == LeaveStatus.Cancelled)
                {
                    return Result.Ok();
                }

                if (request.Status == LeaveStatus.Rejected)
                {
                    return Result.Fail("Une demande refusée ne peut pas être annulée.", "Leave_NotCancellable");
                }

                uow.BeginTransaction();
                try
                {
                    bool wasApproved = request.Status == LeaveStatus.Approved;

                    request.Status = LeaveStatus.Cancelled;
                    request.DecisionNote = note;
                    request.DecidedAtUtc = DateTime.UtcNow;
                    uow.Leave.Update(request);

                    if (wasApproved)
                    {
                        RemoveAttendance(uow, request);
                    }

                    uow.Commit();
                    return Result.Ok();
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public Result Delete(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                LeaveRequest request = uow.Leave.GetById(id);
                if (request == null)
                {
                    return Result.Ok();
                }

                uow.BeginTransaction();
                try
                {
                    if (request.Status == LeaveStatus.Approved)
                    {
                        RemoveAttendance(uow, request);
                    }

                    uow.Leave.SoftDelete(id);
                    uow.Commit();
                    return Result.Ok();
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public LeaveRequest Get(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Leave.GetById(id);
            }
        }

        public IReadOnlyList<LeaveRequest> GetByEmployee(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Leave.GetByEmployee(employeeId).ToList();
            }
        }

        public IReadOnlyList<LeaveRequest> GetByCompanyYear(long companyId, int year)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Leave.GetByCompanyRange(companyId, FirstOfYear(year), LastOfYear(year)).ToList();
            }
        }

        public LeaveBalance GetBalance(long employeeId, int year)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                LeaveSettings settings = ReadSettings(uow);
                Employee employee = uow.Employees.GetById(employeeId);
                List<LeaveRequest> requests =
                    uow.Leave.GetByEmployeeRange(employeeId, FirstOfYear(year), LastOfYear(year)).ToList();

                LeaveBalance balance = BuildBalance(employee, requests, year, settings);
                balance.EmployeeId = employeeId;
                return balance;
            }
        }

        public IReadOnlyList<LeaveBalance> GetCompanyBalances(long companyId, int year)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                LeaveSettings settings = ReadSettings(uow);
                List<LeaveRequest> all =
                    uow.Leave.GetByCompanyRange(companyId, FirstOfYear(year), LastOfYear(year)).ToList();
                var byEmployee = all.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

                var result = new List<LeaveBalance>();
                foreach (Employee employee in uow.Employees.GetByCompany(companyId))
                {
                    List<LeaveRequest> forEmployee = byEmployee.TryGetValue(employee.Id, out var rows)
                        ? rows
                        : new List<LeaveRequest>();

                    LeaveBalance balance = BuildBalance(employee, forEmployee, year, settings);
                    balance.EmployeeId = employee.Id;
                    balance.EmployeeName = (employee.LastNameFr + " " + employee.FirstNameFr).Trim();
                    result.Add(balance);
                }

                return result;
            }
        }

        public decimal CountDays(DateTime start, DateTime end)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return Count(start, end, ReadSettings(uow));
            }
        }

        public LeaveSettings GetSettings()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return ReadSettings(uow);
            }
        }

        public Result SaveSettings(LeaveSettings settings)
        {
            if (settings == null)
            {
                return Result.Fail("Paramètres manquants.", "Leave_SettingsRequired");
            }

            if (settings.DaysPerMonth <= 0m || settings.DaysPerMonth > 10m)
            {
                return Result.Fail("Jours acquis par mois invalide.", "Leave_DaysPerMonthInvalid");
            }

            if (settings.AnnualCap <= 0m || settings.AnnualCap > 365m)
            {
                return Result.Fail("Plafond annuel invalide.", "Leave_CapInvalid");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.AppSettings.Upsert(KeyDaysPerMonth, settings.DaysPerMonth.ToString(CultureInfo.InvariantCulture));
                uow.AppSettings.Upsert(KeyAnnualCap, settings.AnnualCap.ToString(CultureInfo.InvariantCulture));
                uow.AppSettings.Upsert(KeyExcludeRest, settings.ExcludeRestDays ? "1" : "0");
                return Result.Ok();
            }
        }

        // -- cross-module synchronisation --------------------------------------

        /// <summary>
        /// Mirrors an approved request into the Attendance module: one row per leave
        /// day, marked so the module can recognise its own rows later. Unpaid leave is
        /// written as "Absent" so payroll deducts it; every other type as "Congé".
        /// An existing attendance day is never silently overwritten.
        /// </summary>
        private static void WriteAttendance(IUnitOfWork uow, LeaveRequest request, LeaveSettings settings)
        {
            AttendanceStatus status = request.Type == LeaveType.Unpaid
                ? AttendanceStatus.Absent
                : AttendanceStatus.Leave;

            string note = AttendanceMarker + " " + TypeLabel(request.Type);

            foreach (DateTime day in EachLeaveDay(request.StartDate, request.EndDate, settings))
            {
                AttendanceRecord existing = uow.Attendance.GetByEmployeeAndDate(request.EmployeeId, day);
                if (existing != null)
                {
                    existing.Status = status;
                    existing.CheckIn = null;
                    existing.CheckOut = null;
                    existing.WorkedHours = 0m;
                    existing.LateMinutes = 0;
                    existing.OvertimeHours = 0m;
                    existing.Notes = note;
                    uow.Attendance.Update(existing);
                    continue;
                }

                uow.Attendance.Insert(new AttendanceRecord
                {
                    EmployeeId = request.EmployeeId,
                    WorkDate = day,
                    Status = status,
                    WorkedHours = 0m,
                    LateMinutes = 0,
                    OvertimeHours = 0m,
                    Notes = note
                });
            }
        }

        /// <summary>Removes the attendance days this module created for the request.</summary>
        private static void RemoveAttendance(IUnitOfWork uow, LeaveRequest request)
        {
            List<AttendanceRecord> days = uow.Attendance
                .GetByEmployeeRange(request.EmployeeId, request.StartDate, request.EndDate)
                .ToList();

            foreach (AttendanceRecord day in days)
            {
                // Only rows written by this module — a manually recorded day stays.
                if (day.Notes != null && day.Notes.StartsWith(AttendanceMarker, StringComparison.Ordinal))
                {
                    uow.Attendance.SoftDelete(day.Id);
                }
            }
        }

        // -- internals ---------------------------------------------------------

        private static Result Validate(IUnitOfWork uow, LeaveRequest request)
        {
            if (request.EmployeeId <= 0 || !uow.Employees.ExistsById(request.EmployeeId))
            {
                return Result.Fail("Employé introuvable.", "Leave_EmployeeNotFound");
            }

            if (request.StartDate == default(DateTime) || request.EndDate == default(DateTime))
            {
                return Result.Fail("Les dates de début et de fin sont obligatoires.", "Leave_DatesRequired");
            }

            if (request.EndDate.Date < request.StartDate.Date)
            {
                return Result.Fail("La date de fin doit suivre la date de début.", "Leave_EndBeforeStart");
            }

            if ((request.EndDate.Date - request.StartDate.Date).TotalDays > 365)
            {
                return Result.Fail("La période ne peut pas dépasser une année.", "Leave_RangeTooLong");
            }

            // No two live requests of the same employee may cover the same day.
            IEnumerable<LeaveRequest> overlapping =
                uow.Leave.GetByEmployeeRange(request.EmployeeId, request.StartDate.Date, request.EndDate.Date);

            foreach (LeaveRequest other in overlapping)
            {
                if (other.Id == request.Id) continue;
                if (other.Status == LeaveStatus.Rejected || other.Status == LeaveStatus.Cancelled) continue;

                return Result.Fail(
                    "Une autre demande couvre déjà cette période (" +
                    other.StartDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " – " +
                    other.EndDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + ").",
                    "Leave_Overlap");
            }

            return Result.Ok();
        }

        /// <summary>Leave days in a range: rest days (Friday/Saturday) excluded when configured.</summary>
        private static decimal Count(DateTime start, DateTime end, LeaveSettings settings)
        {
            int days = 0;
            foreach (DateTime unused in EachLeaveDay(start, end, settings)) days++;
            return days;
        }

        private static IEnumerable<DateTime> EachLeaveDay(DateTime start, DateTime end, LeaveSettings settings)
        {
            for (DateTime day = start.Date; day <= end.Date; day = day.AddDays(1))
            {
                if (settings.ExcludeRestDays && IsRestDay(day)) continue;
                yield return day;
            }
        }

        /// <summary>The Algerian weekly rest: Friday and Saturday.</summary>
        private static bool IsRestDay(DateTime day)
        {
            return day.DayOfWeek == DayOfWeek.Friday || day.DayOfWeek == DayOfWeek.Saturday;
        }

        private static LeaveBalance BuildBalance(
            Employee employee, IEnumerable<LeaveRequest> requests, int year, LeaveSettings settings)
        {
            var balance = new LeaveBalance { Year = year, Entitlement = 0m };

            if (employee != null)
            {
                balance.Entitlement = Entitlement(employee, year, settings);
            }

            foreach (LeaveRequest request in requests)
            {
                // Only the part of the request falling inside the year counts.
                decimal days = Count(
                    Max(request.StartDate, FirstOfYear(year)),
                    Min(request.EndDate, LastOfYear(year)),
                    settings);

                if (days <= 0m) continue;

                if (request.Status == LeaveStatus.Pending)
                {
                    if (request.Type == LeaveType.Annual) balance.Pending += days;
                    continue;
                }

                if (request.Status != LeaveStatus.Approved) continue;

                if (request.Type == LeaveType.Annual)
                {
                    balance.Taken += days;
                }
                else
                {
                    balance.OtherLeaveDays += days;
                    if (request.Type == LeaveType.Unpaid) balance.UnpaidDays += days;
                }
            }

            balance.Remaining = balance.Entitlement - balance.Taken;
            return balance;
        }

        /// <summary>2,5 days per month worked in the year, capped at 30 (loi 90-11 art. 41).</summary>
        private static decimal Entitlement(Employee employee, int year, LeaveSettings settings)
        {
            DateTime yearStart = FirstOfYear(year);
            DateTime yearEnd = LastOfYear(year);

            DateTime from = Max(employee.HireDate.Date, yearStart);
            DateTime to = employee.ExitDate.HasValue ? Min(employee.ExitDate.Value.Date, yearEnd) : yearEnd;

            if (to < from) return 0m;

            // Complete months of presence inside the year.
            int months = ((to.Year - from.Year) * 12) + to.Month - from.Month;
            if (to.Day >= from.Day) months++;
            if (months < 0) months = 0;
            if (months > 12) months = 12;

            decimal earned = months * settings.DaysPerMonth;
            return earned > settings.AnnualCap ? settings.AnnualCap : earned;
        }

        private static LeaveSettings ReadSettings(IUnitOfWork uow)
        {
            var settings = new LeaveSettings();

            AppSetting perMonth = uow.AppSettings.Get(KeyDaysPerMonth);
            if (perMonth != null && decimal.TryParse(perMonth.SettingValue, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out decimal d) && d > 0m && d <= 10m)
            {
                settings.DaysPerMonth = d;
            }

            AppSetting cap = uow.AppSettings.Get(KeyAnnualCap);
            if (cap != null && decimal.TryParse(cap.SettingValue, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out decimal c) && c > 0m && c <= 365m)
            {
                settings.AnnualCap = c;
            }

            AppSetting exclude = uow.AppSettings.Get(KeyExcludeRest);
            if (exclude != null)
            {
                settings.ExcludeRestDays = exclude.SettingValue != "0";
            }

            return settings;
        }

        internal static string TypeLabel(LeaveType type)
        {
            switch (type)
            {
                case LeaveType.Annual: return "Congé annuel";
                case LeaveType.Sick: return "Congé maladie";
                case LeaveType.Unpaid: return "Congé sans solde";
                case LeaveType.Maternity: return "Congé maternité";
                case LeaveType.Special: return "Congé exceptionnel";
                default: return "Congé";
            }
        }

        private static DateTime FirstOfYear(int year) => new DateTime(year, 1, 1);
        private static DateTime LastOfYear(int year) => new DateTime(year, 12, 31);
        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    }
}
