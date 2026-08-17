using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using WeekendConfig = OptiPaie.Core.Certificates.WeekendConfig;

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
        private const string KeyWeekendDays = "Leave.WeekendDays";

        /// <summary>Marks the attendance rows this module owns, so it only removes its own.</summary>
        private const string AttendanceMarker = "[Congé]";

        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public LeaveService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        /// <summary>Optional audit sink (no-op unless wired by composition). Records lifecycle changes.</summary>
        public IAuditSink Audit { get; set; } = NullAuditSink.Instance;

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

                    // Editable only while a draft or a pending request (both stored at
                    // Status=Pending); an approved/refused/cancelled request is frozen.
                    if (existing.Status != LeaveStatus.Pending)
                    {
                        return Result.Fail<long>(
                            "Seule une demande en attente ou un brouillon peut être modifié.", "Leave_NotEditable");
                    }

                    request.CreatedAtUtc = existing.CreatedAtUtc;
                }

                // A live request stays at Status=Pending; the draft/submitted distinction is
                // carried by IsDraft (respected from the caller — the editor sets it).
                request.Status = LeaveStatus.Pending;

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

                // Submitting straight away (not a draft) reserves the annual balance, so the
                // days that consume it must still be available. A draft reserves nothing.
                if (!request.IsDraft)
                {
                    Result balance = ValidateAnnualBalance(uow, request, settings);
                    if (balance.IsFailure)
                    {
                        return Result.Fail<long>(balance.Error, balance.ErrorCode);
                    }
                }

                if (request.Id > 0)
                {
                    uow.Leave.Update(request);
                    Audit.Record("Leave", request.Id, AuditAction.Updated, "Demande de congé modifiée");
                    return Result.Ok(request.Id);
                }

                long newId = uow.Leave.Insert(request);
                Audit.Record("Leave", newId, AuditAction.Created,
                    request.IsDraft ? "Brouillon de congé créé" : "Demande de congé créée",
                    null, request.IsDraft ? "Brouillon" : "En attente");
                return Result.Ok(newId);
            }
        }

        public Result Submit(long id)
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
                    return Result.Fail("Seul un brouillon peut être soumis.", "Leave_NotDraft");
                }

                if (!request.IsDraft)
                {
                    return Result.Ok(); // already submitted — idempotent
                }

                LeaveSettings settings = ReadSettings(uow);

                // Becomes a LIVE request: re-validate with the overlap rule and reserve the balance.
                request.IsDraft = false;

                Result validation = Validate(uow, request);
                if (validation.IsFailure)
                {
                    return Result.Fail(validation.Error, validation.ErrorCode);
                }

                Result balance = ValidateAnnualBalance(uow, request, settings);
                if (balance.IsFailure)
                {
                    return Result.Fail(balance.Error, balance.ErrorCode);
                }

                request.UpdatedAtUtc = DateTime.UtcNow;
                uow.Leave.Update(request);
                Audit.Record("Leave", id, AuditAction.StatusChanged, "Congé soumis", "Brouillon", "En attente");
                return Result.Ok();
            }
        }

        public Result ReturnToDraft(long id, string note)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                LeaveRequest request = uow.Leave.GetById(id);
                if (request == null)
                {
                    return Result.Fail("Demande introuvable.", "Leave_NotFound");
                }

                if (request.Status != LeaveStatus.Pending || request.IsDraft)
                {
                    return Result.Fail(
                        "Seule une demande en attente peut être renvoyée en brouillon.", "Leave_NotPending");
                }

                request.IsDraft = true;                 // releases the reservation (a draft reserves nothing)
                request.DecisionNote = note;
                request.UpdatedAtUtc = DateTime.UtcNow;
                uow.Leave.Update(request);
                Audit.Record("Leave", id, AuditAction.Returned, "Congé renvoyé pour correction", "En attente", "Brouillon");
                return Result.Ok();
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

                if (request.Status != LeaveStatus.Pending || request.IsDraft)
                {
                    return Result.Fail(
                        request.IsDraft
                            ? "Ce brouillon doit d'abord être soumis."
                            : "Seule une demande en attente peut être approuvée.",
                        "Leave_NotPending");
                }

                LeaveSettings settings = ReadSettings(uow);

                // Defensive: the annual balance must still fit at decision time.
                Result approvalBalance = ValidateAnnualBalance(uow, request, settings);
                if (approvalBalance.IsFailure)
                {
                    return Result.Fail(approvalBalance.Error, approvalBalance.ErrorCode);
                }

                uow.BeginTransaction();
                try
                {
                    request.Status = LeaveStatus.Approved;
                    request.DecisionNote = note;
                    request.DecidedAtUtc = DateTime.UtcNow;
                    uow.Leave.Update(request);

                    WriteAttendance(uow, request, settings);

                    uow.Commit();
                    Audit.Record("Leave", id, AuditAction.Approved, "Congé approuvé", "En attente", "Approuvé");
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

                if (request.Status != LeaveStatus.Pending || request.IsDraft)
                {
                    return Result.Fail("Seule une demande en attente peut être refusée.", "Leave_NotPending");
                }

                if (string.IsNullOrWhiteSpace(note))
                {
                    return Result.Fail("Un motif de refus est obligatoire.", "Leave_ReasonRequired");
                }

                request.Status = LeaveStatus.Rejected;
                request.DecisionNote = note;
                request.DecidedAtUtc = DateTime.UtcNow;
                uow.Leave.Update(request);
                Audit.Record("Leave", id, AuditAction.Rejected, "Congé refusé", "En attente", "Refusé");
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

                // Spec: only an approved leave can be cancelled (Approuvée → Annulée). A pending
                // request is refused or returned to draft; a draft is deleted.
                if (request.Status != LeaveStatus.Approved)
                {
                    return Result.Fail(
                        "Seul un congé approuvé peut être annulé (utilisez Refuser ou Renvoyer pour une demande en attente).",
                        "Leave_NotCancellable");
                }

                if (string.IsNullOrWhiteSpace(note))
                {
                    return Result.Fail("Un motif d'annulation est obligatoire.", "Leave_ReasonRequired");
                }

                uow.BeginTransaction();
                try
                {
                    request.Status = LeaveStatus.Cancelled;
                    request.DecisionNote = note;
                    request.DecidedAtUtc = DateTime.UtcNow;
                    uow.Leave.Update(request);

                    // It was approved, so it had written its days into attendance — take them back.
                    RemoveAttendance(uow, request);

                    uow.Commit();
                    Audit.Record("Leave", id, AuditAction.StatusChanged, "Congé annulé", "Approuvé", "Annulé");
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

                LeaveStatus previous = request.Status;
                uow.BeginTransaction();
                try
                {
                    if (request.Status == LeaveStatus.Approved)
                    {
                        RemoveAttendance(uow, request);
                    }

                    uow.Leave.SoftDelete(id);
                    uow.Commit();
                    Audit.Record("Leave", id, AuditAction.Deleted, "Demande de congé supprimée",
                        previous.ToString(), "Supprimé");
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

                ISet<DayOfWeek> weekendDays = settings.WeekendDays != null && settings.WeekendDays.Count > 0
                    ? settings.WeekendDays
                    : new HashSet<DayOfWeek> { DayOfWeek.Friday, DayOfWeek.Saturday };
                uow.AppSettings.Upsert(KeyWeekendDays, new WeekendConfig(weekendDays).ToStorageString());

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
            AttendanceStatus status = LeaveTypePolicy.IsPaid(request.Type)
                ? AttendanceStatus.Leave
                : AttendanceStatus.Absent;

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
            if (request.EmployeeId <= 0)
            {
                return Result.Fail("Employé introuvable.", "Leave_EmployeeNotFound");
            }

            Employee employee = uow.Employees.GetById(request.EmployeeId);
            if (employee == null)
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

            // The employee must be employed on the leave's start date (actif à la date de début).
            if (employee.HireDate.Date > request.StartDate.Date)
            {
                return Result.Fail(
                    "L'employé n'est pas encore recruté à la date de début du congé.", "Leave_EmployeeInactive");
            }

            if (employee.ExitDate.HasValue && employee.ExitDate.Value.Date < request.StartDate.Date)
            {
                return Result.Fail(
                    "L'employé a quitté l'entreprise avant la date de début du congé.", "Leave_EmployeeInactive");
            }

            // The overlap rule applies to LIVE requests only. A Brouillon (draft) reserves nothing
            // and is not "live": it neither blocks others nor is blocked here. The rule is enforced
            // when it is submitted (Submit sets IsDraft=false, which re-runs this check).
            if (!request.IsDraft)
            {
                IEnumerable<LeaveRequest> overlapping =
                    uow.Leave.GetByEmployeeRange(request.EmployeeId, request.StartDate.Date, request.EndDate.Date);

                foreach (LeaveRequest other in overlapping)
                {
                    if (other.Id == request.Id) continue;
                    if (other.Status == LeaveStatus.Rejected || other.Status == LeaveStatus.Cancelled) continue;
                    if (other.IsDraft) continue; // a draft is not live for the overlap rule

                    return Result.Fail(
                        "Une autre demande couvre déjà cette période (" +
                        other.StartDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " – " +
                        other.EndDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + ").",
                        "Leave_Overlap");
                }
            }

            return Result.Ok();
        }

        /// <summary>
        /// For a type that consumes the annual entitlement (Congé annuel), the requested days must
        /// fit the AVAILABLE balance (acquis − consommé − réservé) of every year the request touches.
        /// The balance is computed from all OTHER requests (the request never counts against itself),
        /// so re-validating at submit or approval is idempotent. Non-consuming types always pass.
        /// </summary>
        private static Result ValidateAnnualBalance(IUnitOfWork uow, LeaveRequest request, LeaveSettings settings)
        {
            if (!LeaveTypePolicy.DecrementsAnnualBalance(request.Type))
            {
                return Result.Ok();
            }

            Employee employee = uow.Employees.GetById(request.EmployeeId);

            for (int year = request.StartDate.Year; year <= request.EndDate.Year; year++)
            {
                decimal requestedInYear = Count(
                    Max(request.StartDate.Date, FirstOfYear(year)),
                    Min(request.EndDate.Date, LastOfYear(year)),
                    settings);

                if (requestedInYear <= 0m) continue;

                List<LeaveRequest> others = uow.Leave
                    .GetByEmployeeRange(request.EmployeeId, FirstOfYear(year), LastOfYear(year))
                    .Where(r => r.Id != request.Id)
                    .ToList();

                LeaveBalance balance = BuildBalance(employee, others, year, settings);

                if (requestedInYear > balance.Available)
                {
                    return Result.Fail(
                        "Solde de congé annuel insuffisant pour " + year.ToString(CultureInfo.InvariantCulture) +
                        " : demandé " + Fmt(requestedInYear) + " j, disponible " + Fmt(balance.Available) + " j.",
                        "Leave_InsufficientBalance");
                }
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
                if (settings.ExcludeRestDays && IsRestDay(day, settings)) continue;
                yield return day;
            }
        }

        /// <summary>A weekly rest day per the company parameter (default: Friday + Saturday).</summary>
        private static bool IsRestDay(DateTime day, LeaveSettings settings)
        {
            ISet<DayOfWeek> weekend = settings.WeekendDays;
            if (weekend == null || weekend.Count == 0)
            {
                // Empty/undefined set → fall back to the Algerian default rather than counting
                // every day as worked (turning exclusion off is done via ExcludeRestDays).
                return day.DayOfWeek == DayOfWeek.Friday || day.DayOfWeek == DayOfWeek.Saturday;
            }

            return weekend.Contains(day.DayOfWeek);
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
                    // A SUBMITTED (non-draft) pending request RESERVES the annual balance;
                    // a Brouillon (draft) reserves nothing.
                    if (!request.IsDraft && LeaveTypePolicy.DecrementsAnnualBalance(request.Type))
                    {
                        balance.Pending += days;
                    }

                    continue;
                }

                if (request.Status != LeaveStatus.Approved) continue;

                if (LeaveTypePolicy.DecrementsAnnualBalance(request.Type))
                {
                    balance.Taken += days;
                }
                else
                {
                    balance.OtherLeaveDays += days;
                    if (!LeaveTypePolicy.IsPaid(request.Type)) balance.UnpaidDays += days;
                }
            }

            balance.Remaining = balance.Entitlement - balance.Taken;
            balance.Available = balance.Entitlement - balance.Taken - balance.Pending;
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

            AppSetting weekend = uow.AppSettings.Get(KeyWeekendDays);
            if (weekend != null && !string.IsNullOrWhiteSpace(weekend.SettingValue))
            {
                try
                {
                    settings.WeekendDays = new HashSet<DayOfWeek>(
                        WeekendConfig.FromStorageString(weekend.SettingValue).WeekendDays);
                }
                catch
                {
                    // Malformed value — keep the Friday/Saturday default.
                }
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

        private static string Fmt(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static DateTime FirstOfYear(int year) => new DateTime(year, 1, 1);
        private static DateTime LastOfYear(int year) => new DateTime(year, 12, 31);
        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    }
}
