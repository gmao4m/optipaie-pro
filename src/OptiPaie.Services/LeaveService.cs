using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Common.Logging;
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
    /// Leave module orchestration. Owns ALL leave rules so every screen and the payroll chain agree.
    /// Every regulatory option is a company setting that DEFAULTS to the historical behaviour, so an
    /// existing database opens and computes exactly as before; a company opts in to the legal rule.
    /// Approving writes the days into Attendance (the payroll engine is never touched).
    /// </summary>
    public sealed class LeaveService : ILeaveService
    {
        private const string KeyDaysPerMonth = "Leave.DaysPerMonth";
        private const string KeyAnnualCap = "Leave.AnnualCap";
        private const string KeyExcludeRest = "Leave.ExcludeRestDays";
        private const string KeyWeekendDays = "Leave.WeekendDays";
        private const string KeyMaternityDays = "Leave.MaternityDays";
        // Per-company regulatory flags (suffixed with the company id); absent = historical default.
        private const string KeyExcludeHolidays = "Leave.ExcludeHolidays";
        private const string KeyCalendarCount = "Leave.CalendarDayCount";
        private const string KeyRefJulyJune = "Leave.ReferenceJulyToJune";
        private const string KeyAccrualExclUnpaid = "Leave.AccrualExcludesUnpaid";
        private const string KeyStrictCnas = "Leave.StrictCnasTreatment";

        /// <summary>Marks the attendance rows this module owns, so it only removes its own.</summary>
        private const string AttendanceMarker = "[Congé]";

        private static readonly ISet<DateTime> NoHolidays = new HashSet<DateTime>();

        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public LeaveService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        /// <summary>Optional audit sink (no-op unless wired by composition). Records lifecycle changes.</summary>
        public IAuditSink Audit { get; set; } = NullAuditSink.Instance;

        /// <summary>Optional logger — every refusal is written here (never a silent failure).</summary>
        public ILogger Logger { get; set; }

        // ---- resolved-per-operation context (settings + holidays + configurable types) ----
        private sealed class Ctx
        {
            public LeaveSettings Settings;
            public ISet<DateTime> Holidays;
            public Dictionary<long, LeaveTypeDefinition> Types;
        }

        public Result<long> Save(LeaveRequest request)
        {
            if (request == null)
            {
                return Fail<long>("Aucune demande de congé.", "Leave_Required");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                long companyId = CompanyOf(uow, request.EmployeeId);
                Ctx ctx = BuildCtx(uow, companyId, request.StartDate, request.EndDate);

                if (request.Id > 0)
                {
                    LeaveRequest existing = uow.Leave.GetById(request.Id);
                    if (existing == null)
                    {
                        return Fail<long>("Demande introuvable.", "Leave_NotFound");
                    }

                    if (existing.Status != LeaveStatus.Pending)
                    {
                        return Fail<long>("Seule une demande en attente ou un brouillon peut être modifié.", "Leave_NotEditable");
                    }

                    request.CreatedAtUtc = existing.CreatedAtUtc;
                }

                request.Status = LeaveStatus.Pending;

                Result validation = Validate(uow, request);
                if (validation.IsFailure)
                {
                    return Fail<long>(validation.Error, validation.ErrorCode);
                }

                request.StartDate = request.StartDate.Date;
                request.EndDate = request.EndDate.Date;
                request.Days = Count(request.StartDate, request.EndDate, ctx);

                if (request.Days <= 0m)
                {
                    return Fail<long>("La période ne contient aucun jour décompté (repos/fériés uniquement).", "Leave_NoWorkingDay");
                }

                if (!request.IsDraft)
                {
                    Result balance = ValidateAnnualBalance(uow, request, ctx);
                    if (balance.IsFailure)
                    {
                        return Fail<long>(balance.Error, balance.ErrorCode);
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
                if (request == null) return Fail("Demande introuvable.", "Leave_NotFound");
                if (request.Status != LeaveStatus.Pending) return Fail("Seul un brouillon peut être soumis.", "Leave_NotDraft");
                if (!request.IsDraft) return Result.Ok(); // already submitted — idempotent

                Ctx ctx = BuildCtx(uow, CompanyOf(uow, request.EmployeeId), request.StartDate, request.EndDate);
                request.IsDraft = false;

                Result validation = Validate(uow, request);
                if (validation.IsFailure) return Fail(validation.Error, validation.ErrorCode);

                Result balance = ValidateAnnualBalance(uow, request, ctx);
                if (balance.IsFailure) return Fail(balance.Error, balance.ErrorCode);

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
                if (request == null) return Fail("Demande introuvable.", "Leave_NotFound");
                if (request.Status != LeaveStatus.Pending || request.IsDraft)
                    return Fail("Seule une demande en attente peut être renvoyée en brouillon.", "Leave_NotPending");

                request.IsDraft = true;
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
                if (request == null) return Fail("Demande introuvable.", "Leave_NotFound");
                if (request.Status == LeaveStatus.Approved) return Result.Ok();
                if (request.Status != LeaveStatus.Pending || request.IsDraft)
                    return Fail(request.IsDraft ? "Ce brouillon doit d'abord être soumis." : "Seule une demande en attente peut être approuvée.", "Leave_NotPending");

                Ctx ctx = BuildCtx(uow, CompanyOf(uow, request.EmployeeId), request.StartDate, request.EndDate);

                Result approvalBalance = ValidateAnnualBalance(uow, request, ctx);
                if (approvalBalance.IsFailure) return Fail(approvalBalance.Error, approvalBalance.ErrorCode);

                uow.BeginTransaction();
                try
                {
                    request.Status = LeaveStatus.Approved;
                    request.DecisionNote = note;
                    request.DecidedAtUtc = DateTime.UtcNow;
                    uow.Leave.Update(request);

                    WriteAttendance(uow, request, ctx);

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
                if (request == null) return Fail("Demande introuvable.", "Leave_NotFound");
                if (request.Status != LeaveStatus.Pending || request.IsDraft)
                    return Fail("Seule une demande en attente peut être refusée.", "Leave_NotPending");
                if (string.IsNullOrWhiteSpace(note))
                    return Fail("Un motif de refus est obligatoire.", "Leave_ReasonRequired");

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
                if (request == null) return Fail("Demande introuvable.", "Leave_NotFound");
                if (request.Status == LeaveStatus.Cancelled) return Result.Ok();
                if (request.Status != LeaveStatus.Approved)
                    return Fail("Seul un congé approuvé peut être annulé (utilisez Refuser ou Renvoyer pour une demande en attente).", "Leave_NotCancellable");
                if (string.IsNullOrWhiteSpace(note))
                    return Fail("Un motif d'annulation est obligatoire.", "Leave_ReasonRequired");

                uow.BeginTransaction();
                try
                {
                    request.Status = LeaveStatus.Cancelled;
                    request.DecisionNote = note;
                    request.DecidedAtUtc = DateTime.UtcNow;
                    uow.Leave.Update(request);
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
                if (request == null) return Result.Ok();

                LeaveStatus previous = request.Status;
                uow.BeginTransaction();
                try
                {
                    if (request.Status == LeaveStatus.Approved) RemoveAttendance(uow, request);
                    uow.Leave.SoftDelete(id);
                    uow.Commit();
                    Audit.Record("Leave", id, AuditAction.Deleted, "Demande de congé supprimée", previous.ToString(), "Supprimé");
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
            using (IUnitOfWork uow = _unitOfWorkFactory.Create()) return uow.Leave.GetById(id);
        }

        public IReadOnlyList<LeaveRequest> GetByEmployee(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create()) return uow.Leave.GetByEmployee(employeeId).ToList();
        }

        public IReadOnlyList<LeaveRequest> GetByCompanyYear(long companyId, int year)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
                return uow.Leave.GetByCompanyRange(companyId, FirstOfYear(year), LastOfYear(year)).ToList();
        }

        public LeaveBalance GetBalance(long employeeId, int year)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Employee employee = uow.Employees.GetById(employeeId);
                long companyId = employee != null ? employee.CompanyId : 0L;
                Ctx ctx = BuildCtx(uow, companyId, FirstOfYear(year), LastOfYear(year));
                List<LeaveRequest> requests = uow.Leave.GetByEmployeeRange(employeeId, FirstOfYear(year), LastOfYear(year)).ToList();

                LeaveBalance balance = BuildBalance(employee, requests, year, ctx);
                balance.EmployeeId = employeeId;
                return balance;
            }
        }

        public IReadOnlyList<LeaveBalance> GetCompanyBalances(long companyId, int year)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Ctx ctx = BuildCtx(uow, companyId, FirstOfYear(year), LastOfYear(year));
                List<LeaveRequest> all = uow.Leave.GetByCompanyRange(companyId, FirstOfYear(year), LastOfYear(year)).ToList();
                var byEmployee = all.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

                var result = new List<LeaveBalance>();
                foreach (Employee employee in uow.Employees.GetByCompany(companyId))
                {
                    List<LeaveRequest> forEmployee = byEmployee.TryGetValue(employee.Id, out var rows) ? rows : new List<LeaveRequest>();
                    LeaveBalance balance = BuildBalance(employee, forEmployee, year, ctx);
                    balance.EmployeeId = employee.Id;
                    balance.EmployeeName = (employee.LastNameFr + " " + employee.FirstNameFr).Trim();
                    result.Add(balance);
                }

                return result;
            }
        }

        public decimal CountDays(DateTime start, DateTime end)
        {
            // Company-agnostic quick count (week-end per global settings, no holidays). Preview() is the
            // employee-accurate one (it applies holidays and the company's options).
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                var ctx = new Ctx { Settings = ReadSettings(uow, 0), Holidays = NoHolidays, Types = new Dictionary<long, LeaveTypeDefinition>() };
                return Count(start, end, ctx);
            }
        }

        public LeavePreview Preview(LeaveRequest request)
        {
            var preview = new LeavePreview();
            if (request == null) { preview.Reason = "Aucune demande."; preview.ReasonCode = "Leave_Required"; return preview; }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                long companyId = CompanyOf(uow, request.EmployeeId);
                Ctx ctx = BuildCtx(uow, companyId, request.StartDate, request.EndDate);

                preview.Category = ResolveCategory(request, ctx);
                preview.DecrementsBalance = ResolveDecrements(request, ctx);
                preview.Days = Count(request.StartDate.Date, request.EndDate.Date, ctx);

                LeaveBalance before = GetBalanceInternal(uow, request.EmployeeId, request.StartDate.Year, ctx);
                preview.AvailableBefore = before.Available;
                preview.AvailableAfter = preview.DecrementsBalance ? before.Available - preview.Days : before.Available;

                // Blocking checks, most specific first, each with its precise reason.
                Result validation = Validate(uow, request);
                if (validation.IsFailure) { preview.Ok = false; preview.Reason = validation.Error; preview.ReasonCode = validation.ErrorCode; return preview; }
                if (preview.Days <= 0m) { preview.Ok = false; preview.Reason = "La période ne contient aucun jour décompté."; preview.ReasonCode = "Leave_NoWorkingDay"; return preview; }
                Result balance = ValidateAnnualBalance(uow, request, ctx);
                if (balance.IsFailure) { preview.Ok = false; preview.Reason = balance.Error; preview.ReasonCode = balance.ErrorCode; return preview; }

                preview.Ok = true;
                return preview;
            }
        }

        public IReadOnlyList<AccrualMonth> GetAccrualDetail(long employeeId, int year)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Employee employee = uow.Employees.GetById(employeeId);
                long companyId = employee != null ? employee.CompanyId : 0L;
                Ctx ctx = BuildCtx(uow, companyId, FirstOfYear(year), LastOfYear(year));
                List<LeaveRequest> requests = uow.Leave.GetByEmployeeRange(employeeId, FirstOfYear(year), LastOfYear(year)).ToList();
                return AccrualMonths(employee, year, ctx, requests);
            }
        }

        public FinalSettlement ComputeFinalSettlement(long employeeId, DateTime exitDate)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Employee employee = uow.Employees.GetById(employeeId);
                long companyId = employee != null ? employee.CompanyId : 0L;
                int year = exitDate.Year;
                Ctx ctx = BuildCtx(uow, companyId, FirstOfYear(year), LastOfYear(year));
                List<LeaveRequest> requests = uow.Leave.GetByEmployeeRange(employeeId, FirstOfYear(year), LastOfYear(year)).ToList();

                // Acquired up to the exit date (prorata), minus annual days already taken this year.
                Employee toExit = employee;
                if (toExit != null && (!toExit.ExitDate.HasValue || toExit.ExitDate.Value.Date > exitDate.Date))
                {
                    // evaluate accrual as if the contract ends on exitDate
                    toExit = CloneWithExit(employee, exitDate);
                }

                decimal acquired = Entitlement(toExit, year, ctx, requests);
                decimal taken = 0m;
                foreach (LeaveRequest r in requests)
                {
                    if (r.Status != LeaveStatus.Approved || !ResolveDecrements(r, ctx)) continue;
                    taken += Count(Max(r.StartDate, FirstOfYear(year)), Min(r.EndDate, LastOfYear(year)), ctx);
                }

                decimal remaining = acquired - taken;
                if (remaining < 0m) remaining = 0m;

                decimal monthly = employee != null ? employee.BaseSalary : 0m;
                decimal daily = monthly / 30m; // usage courant : salaire mensuel / 30
                return new FinalSettlement
                {
                    EmployeeId = employeeId,
                    EmployeeName = employee != null ? (employee.LastNameFr + " " + employee.FirstNameFr).Trim() : string.Empty,
                    ExitDate = exitDate.Date,
                    Acquired = acquired,
                    Taken = taken,
                    RemainingDays = remaining,
                    MonthlySalary = monthly,
                    DailyRate = daily,
                    Amount = decimal.Round(remaining * daily, 2, MidpointRounding.AwayFromZero)
                };
            }
        }

        public IReadOnlyList<LeaveTypeDefinition> GetTypes(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
                return uow.LeaveTypes.GetForCompany(companyId).Where(t => t.IsActive).ToList();
        }

        public LeaveSettings GetSettings()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create()) return ReadSettings(uow, 0);
        }

        public LeaveSettings GetSettings(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create()) return ReadSettings(uow, companyId);
        }

        public Result SaveSettings(LeaveSettings settings) => SaveSettings(0, settings);

        public Result SaveSettings(long companyId, LeaveSettings settings)
        {
            if (settings == null) return Fail("Paramètres manquants.", "Leave_SettingsRequired");
            if (settings.DaysPerMonth <= 0m || settings.DaysPerMonth > 10m) return Fail("Jours acquis par mois invalide.", "Leave_DaysPerMonthInvalid");
            if (settings.AnnualCap <= 0m || settings.AnnualCap > 365m) return Fail("Plafond annuel invalide.", "Leave_CapInvalid");

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                // Historical, global parameters (unchanged).
                uow.AppSettings.Upsert(KeyDaysPerMonth, settings.DaysPerMonth.ToString(CultureInfo.InvariantCulture));
                uow.AppSettings.Upsert(KeyAnnualCap, settings.AnnualCap.ToString(CultureInfo.InvariantCulture));
                uow.AppSettings.Upsert(KeyExcludeRest, settings.ExcludeRestDays ? "1" : "0");
                uow.AppSettings.Upsert(KeyMaternityDays, settings.MaternityDays.ToString(CultureInfo.InvariantCulture));

                ISet<DayOfWeek> weekendDays = settings.WeekendDays != null && settings.WeekendDays.Count > 0
                    ? settings.WeekendDays : new HashSet<DayOfWeek> { DayOfWeek.Friday, DayOfWeek.Saturday };
                uow.AppSettings.Upsert(KeyWeekendDays, new WeekendConfig(weekendDays).ToStorageString());

                // Per-company regulatory flags.
                uow.AppSettings.Upsert(Scoped(KeyExcludeHolidays, companyId), settings.ExcludeHolidays ? "1" : "0");
                uow.AppSettings.Upsert(Scoped(KeyCalendarCount, companyId), settings.CalendarDayCount ? "1" : "0");
                uow.AppSettings.Upsert(Scoped(KeyRefJulyJune, companyId), settings.ReferenceJulyToJune ? "1" : "0");
                uow.AppSettings.Upsert(Scoped(KeyAccrualExclUnpaid, companyId), settings.AccrualExcludesUnpaid ? "1" : "0");
                uow.AppSettings.Upsert(Scoped(KeyStrictCnas, companyId), settings.StrictCnasTreatment ? "1" : "0");

                return Result.Ok();
            }
        }

        // ================================================================ cross-module synchronisation

        private void WriteAttendance(IUnitOfWork uow, LeaveRequest request, Ctx ctx)
        {
            PaymentCategory category = ResolveCategory(request, ctx);
            // A day is "suspended" (Absent → payroll deducts) for unpaid leave, and — ONLY if the company
            // opted in — for CNAS-paid leave. Default: CNAS behaves like employer-paid (salary maintained).
            bool suspend = category == PaymentCategory.Unpaid
                           || (category == PaymentCategory.SocialSecurity && ctx.Settings.StrictCnasTreatment);
            AttendanceStatus status = suspend ? AttendanceStatus.Absent : AttendanceStatus.Leave;

            string note = AttendanceMarker + " " + TypeLabelOf(request, ctx);

            foreach (DateTime day in EachLeaveDay(request.StartDate, request.EndDate, ctx))
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

        private static void RemoveAttendance(IUnitOfWork uow, LeaveRequest request)
        {
            List<AttendanceRecord> days = uow.Attendance
                .GetByEmployeeRange(request.EmployeeId, request.StartDate, request.EndDate).ToList();

            foreach (AttendanceRecord day in days)
            {
                if (day.Notes != null && day.Notes.StartsWith(AttendanceMarker, StringComparison.Ordinal))
                {
                    uow.Attendance.SoftDelete(day.Id);
                }
            }
        }

        // ================================================================ internals

        private static Result Validate(IUnitOfWork uow, LeaveRequest request)
        {
            if (request.EmployeeId <= 0) return Result.Fail("Employé introuvable.", "Leave_EmployeeNotFound");

            Employee employee = uow.Employees.GetById(request.EmployeeId);
            if (employee == null) return Result.Fail("Employé introuvable.", "Leave_EmployeeNotFound");

            if (request.StartDate == default(DateTime) || request.EndDate == default(DateTime))
                return Result.Fail("Les dates de début et de fin sont obligatoires.", "Leave_DatesRequired");
            if (request.EndDate.Date < request.StartDate.Date)
                return Result.Fail("La date de fin doit suivre la date de début.", "Leave_EndBeforeStart");
            if ((request.EndDate.Date - request.StartDate.Date).TotalDays > 365)
                return Result.Fail("La période ne peut pas dépasser une année.", "Leave_RangeTooLong");

            if (employee.HireDate.Date > request.StartDate.Date)
                return Result.Fail("L'employé n'est pas encore recruté à la date de début du congé.", "Leave_EmployeeInactive");
            if (employee.ExitDate.HasValue && employee.ExitDate.Value.Date < request.StartDate.Date)
                return Result.Fail("L'employé a quitté l'entreprise avant la date de début du congé.", "Leave_EmployeeInactive");

            if (!request.IsDraft)
            {
                IEnumerable<LeaveRequest> overlapping =
                    uow.Leave.GetByEmployeeRange(request.EmployeeId, request.StartDate.Date, request.EndDate.Date);

                foreach (LeaveRequest other in overlapping)
                {
                    if (other.Id == request.Id) continue;
                    if (other.Status == LeaveStatus.Rejected || other.Status == LeaveStatus.Cancelled) continue;
                    if (other.IsDraft) continue;

                    return Result.Fail(
                        "Une autre demande couvre déjà cette période (" +
                        other.StartDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " – " +
                        other.EndDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + ").",
                        "Leave_Overlap");
                }
            }

            return Result.Ok();
        }

        private static Result ValidateAnnualBalance(IUnitOfWork uow, LeaveRequest request, Ctx ctx)
        {
            if (!ResolveDecrements(request, ctx)) return Result.Ok();

            Employee employee = uow.Employees.GetById(request.EmployeeId);

            for (int year = request.StartDate.Year; year <= request.EndDate.Year; year++)
            {
                decimal requestedInYear = Count(Max(request.StartDate.Date, FirstOfYear(year)), Min(request.EndDate.Date, LastOfYear(year)), ctx);
                if (requestedInYear <= 0m) continue;

                List<LeaveRequest> others = uow.Leave
                    .GetByEmployeeRange(request.EmployeeId, FirstOfYear(year), LastOfYear(year))
                    .Where(r => r.Id != request.Id).ToList();

                LeaveBalance balance = BuildBalance(employee, others, year, ctx);

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

        private static decimal Count(DateTime start, DateTime end, Ctx ctx)
        {
            int days = 0;
            foreach (DateTime unused in EachLeaveDay(start, end, ctx)) days++;
            return days;
        }

        private static IEnumerable<DateTime> EachLeaveDay(DateTime start, DateTime end, Ctx ctx)
        {
            LeaveSettings s = ctx.Settings;
            bool excludeWeekend = s.ExcludeRestDays && !s.CalendarDayCount;

            for (DateTime day = start.Date; day <= end.Date; day = day.AddDays(1))
            {
                if (excludeWeekend && IsRestDay(day, s)) continue;
                if (s.ExcludeHolidays && ctx.Holidays != null && ctx.Holidays.Contains(day.Date)) continue;
                yield return day;
            }
        }

        private static bool IsRestDay(DateTime day, LeaveSettings settings)
        {
            ISet<DayOfWeek> weekend = settings.WeekendDays;
            if (weekend == null || weekend.Count == 0)
                return day.DayOfWeek == DayOfWeek.Friday || day.DayOfWeek == DayOfWeek.Saturday;
            return weekend.Contains(day.DayOfWeek);
        }

        private static LeaveBalance BuildBalance(Employee employee, IEnumerable<LeaveRequest> requests, int year, Ctx ctx)
        {
            List<LeaveRequest> reqList = requests as List<LeaveRequest> ?? requests.ToList();
            var balance = new LeaveBalance { Year = year, Entitlement = 0m };
            if (employee != null) balance.Entitlement = Entitlement(employee, year, ctx, reqList);

            foreach (LeaveRequest request in reqList)
            {
                decimal days = Count(Max(request.StartDate, FirstOfYear(year)), Min(request.EndDate, LastOfYear(year)), ctx);
                if (days <= 0m) continue;

                if (request.Status == LeaveStatus.Pending)
                {
                    if (!request.IsDraft && ResolveDecrements(request, ctx)) balance.Pending += days;
                    continue;
                }

                if (request.Status != LeaveStatus.Approved) continue;

                if (ResolveDecrements(request, ctx))
                {
                    balance.Taken += days;
                }
                else
                {
                    balance.OtherLeaveDays += days;
                    if (ResolveCategory(request, ctx) == PaymentCategory.Unpaid) balance.UnpaidDays += days;
                }
            }

            balance.Remaining = balance.Entitlement - balance.Taken;
            balance.Available = balance.Entitlement - balance.Taken - balance.Pending;
            return balance;
        }

        /// <summary>
        /// 2,5 days per month worked, capped at 30 (loi 90-11 art. 41). When the company opts in,
        /// months dominated by unpaid leave do not accrue (art. 46). Default OFF → the historical
        /// formula, byte-identical to before.
        /// </summary>
        private static decimal Entitlement(Employee employee, int year, Ctx ctx, IEnumerable<LeaveRequest> requests)
        {
            LeaveSettings s = ctx.Settings;

            if (s.AccrualExcludesUnpaid)
            {
                decimal sum = AccrualMonths(employee, year, ctx, requests as List<LeaveRequest> ?? requests.ToList())
                    .Sum(a => a.Accrued);
                return sum > s.AnnualCap ? s.AnnualCap : sum;
            }

            // Historical formula (unchanged): months of presence in the reference window × 2,5, capped.
            DateTime windowStart = s.ReferenceJulyToJune ? new DateTime(year - 1, 7, 1) : FirstOfYear(year);
            DateTime windowEnd = s.ReferenceJulyToJune ? new DateTime(year, 6, 30) : LastOfYear(year);

            DateTime from = Max(employee.HireDate.Date, windowStart);
            DateTime to = employee.ExitDate.HasValue ? Min(employee.ExitDate.Value.Date, windowEnd) : windowEnd;
            if (to < from) return 0m;

            int months = ((to.Year - from.Year) * 12) + to.Month - from.Month;
            if (to.Day >= from.Day) months++;
            if (months < 0) months = 0;
            if (months > 12) months = 12;

            decimal earned = months * s.DaysPerMonth;
            return earned > s.AnnualCap ? s.AnnualCap : earned;
        }

        /// <summary>Month-by-month accrual view (informative). Unpaid-dominated months show 0 when the option is on.</summary>
        private static IReadOnlyList<AccrualMonth> AccrualMonths(Employee employee, int year, Ctx ctx, List<LeaveRequest> requests)
        {
            var list = new List<AccrualMonth>();
            if (employee == null) return list;

            LeaveSettings s = ctx.Settings;
            for (int m = 1; m <= 12; m++)
            {
                DateTime mStart = new DateTime(year, m, 1);
                DateTime mEnd = mStart.AddMonths(1).AddDays(-1);

                bool present = employee.HireDate.Date <= mEnd && (!employee.ExitDate.HasValue || employee.ExitDate.Value.Date >= mStart);

                decimal unpaidDays = 0m;
                foreach (LeaveRequest r in requests)
                {
                    if (r.Status != LeaveStatus.Approved) continue;
                    if (ResolveCategory(r, ctx) != PaymentCategory.Unpaid) continue;
                    unpaidDays += Count(Max(r.StartDate, mStart), Min(r.EndDate, mEnd), ctx);
                }

                decimal monthWorking = Count(mStart, mEnd, ctx);
                bool unpaidDominated = s.AccrualExcludesUnpaid && monthWorking > 0m && unpaidDays >= monthWorking / 2m;
                decimal accrued = present && !unpaidDominated ? s.DaysPerMonth : 0m;

                list.Add(new AccrualMonth { Month = m, Present = present, UnpaidDays = unpaidDays, Accrued = accrued });
            }

            return list;
        }

        // ---- policy resolution (configurable type, else legacy) ----

        private static bool ResolveDecrements(LeaveRequest r, Ctx ctx)
            => OptiPaie.Core.Leave.LeaveTypeResolver.Decrements(r.LeaveTypeId, r.Type, ctx.Types);

        private static PaymentCategory ResolveCategory(LeaveRequest r, Ctx ctx)
            => OptiPaie.Core.Leave.LeaveTypeResolver.Category(r.LeaveTypeId, r.Type, ctx.Types);

        private static string TypeLabelOf(LeaveRequest r, Ctx ctx)
        {
            if (r.LeaveTypeId.HasValue && ctx.Types != null && ctx.Types.TryGetValue(r.LeaveTypeId.Value, out var def))
                return string.IsNullOrWhiteSpace(def.LabelFr) ? def.LabelAr : def.LabelFr;
            return TypeLabel(r.Type);
        }

        // ---- context building ----

        private Ctx BuildCtx(IUnitOfWork uow, long companyId, DateTime from, DateTime to)
        {
            LeaveSettings settings = ReadSettings(uow, companyId);
            Dictionary<long, LeaveTypeDefinition> types = uow.LeaveTypes.GetForCompany(companyId).ToDictionary(t => t.Id);
            ISet<DateTime> holidays = settings.ExcludeHolidays ? BuildHolidaySet(uow, companyId, from, to) : NoHolidays;
            return new Ctx { Settings = settings, Holidays = holidays, Types = types };
        }

        private LeaveBalance GetBalanceInternal(IUnitOfWork uow, long employeeId, int year, Ctx ctx)
        {
            Employee employee = uow.Employees.GetById(employeeId);
            List<LeaveRequest> requests = uow.Leave.GetByEmployeeRange(employeeId, FirstOfYear(year), LastOfYear(year)).ToList();
            LeaveBalance balance = BuildBalance(employee, requests, year, ctx);
            balance.EmployeeId = employeeId;
            return balance;
        }

        private static ISet<DateTime> BuildHolidaySet(IUnitOfWork uow, long companyId, DateTime from, DateTime to)
        {
            var set = new HashSet<DateTime>();
            for (int y = from.Year; y <= to.Year; y++)
            {
                foreach (DateTime d in FixedCivilHolidays(y))
                    if (d.Date >= from.Date && d.Date <= to.Date) set.Add(d.Date);
            }
            foreach (Holiday h in uow.Holidays.GetForCompanyRange(companyId, from, to)) set.Add(h.HolidayDate.Date);
            return set;
        }

        /// <summary>The five fixed-date Algerian civil holidays of a year (always excluded when the option is on).</summary>
        internal static IEnumerable<DateTime> FixedCivilHolidays(int year)
        {
            yield return new DateTime(year, 1, 1);   // Nouvel An
            yield return new DateTime(year, 1, 12);  // Yennayer
            yield return new DateTime(year, 5, 1);   // Fête du travail
            yield return new DateTime(year, 7, 5);   // Indépendance
            yield return new DateTime(year, 11, 1);  // Anniversaire de la Révolution
        }

        private LeaveSettings ReadSettings(IUnitOfWork uow, long companyId)
        {
            var settings = new LeaveSettings();

            AppSetting perMonth = uow.AppSettings.Get(KeyDaysPerMonth);
            if (perMonth != null && decimal.TryParse(perMonth.SettingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d) && d > 0m && d <= 10m)
                settings.DaysPerMonth = d;

            AppSetting cap = uow.AppSettings.Get(KeyAnnualCap);
            if (cap != null && decimal.TryParse(cap.SettingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal c) && c > 0m && c <= 365m)
                settings.AnnualCap = c;

            AppSetting exclude = uow.AppSettings.Get(KeyExcludeRest);
            if (exclude != null) settings.ExcludeRestDays = exclude.SettingValue != "0";

            AppSetting maternity = uow.AppSettings.Get(KeyMaternityDays);
            if (maternity != null && decimal.TryParse(maternity.SettingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal mat) && mat > 0m && mat <= 400m)
                settings.MaternityDays = mat;

            AppSetting weekend = uow.AppSettings.Get(KeyWeekendDays);
            if (weekend != null && !string.IsNullOrWhiteSpace(weekend.SettingValue))
            {
                try { settings.WeekendDays = new HashSet<DayOfWeek>(WeekendConfig.FromStorageString(weekend.SettingValue).WeekendDays); }
                catch { /* malformed → keep default */ }
            }

            if (companyId > 0)
            {
                settings.ExcludeHolidays = Flag(uow, Scoped(KeyExcludeHolidays, companyId));
                settings.CalendarDayCount = Flag(uow, Scoped(KeyCalendarCount, companyId));
                settings.ReferenceJulyToJune = Flag(uow, Scoped(KeyRefJulyJune, companyId));
                settings.AccrualExcludesUnpaid = Flag(uow, Scoped(KeyAccrualExclUnpaid, companyId));
                settings.StrictCnasTreatment = Flag(uow, Scoped(KeyStrictCnas, companyId));
            }

            return settings;
        }

        private static bool Flag(IUnitOfWork uow, string key)
        {
            AppSetting s = uow.AppSettings.Get(key);
            return s != null && s.SettingValue == "1";
        }

        private static string Scoped(string baseKey, long companyId) => baseKey + "." + companyId.ToString(CultureInfo.InvariantCulture);

        private static Employee CloneWithExit(Employee e, DateTime exit)
        {
            return new Employee
            {
                Id = e.Id, CompanyId = e.CompanyId, HireDate = e.HireDate, ExitDate = exit,
                BaseSalary = e.BaseSalary, LastNameFr = e.LastNameFr, FirstNameFr = e.FirstNameFr
            };
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

        // ---- refusal helpers: return the Result AND log it (never a silent failure) ----

        private Result Fail(string message, string code)
        {
            Logger?.Warn("Congé refusé [" + code + "] : " + message);
            return Result.Fail(message, code);
        }

        private Result<T> Fail<T>(string message, string code)
        {
            Logger?.Warn("Congé refusé [" + code + "] : " + message);
            return Result.Fail<T>(message, code);
        }

        private static string Fmt(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        private static long CompanyOf(IUnitOfWork uow, long employeeId)
        {
            Employee e = uow.Employees.GetById(employeeId);
            return e != null ? e.CompanyId : 0L;
        }

        private static DateTime FirstOfYear(int year) => new DateTime(year, 1, 1);
        private static DateTime LastOfYear(int year) => new DateTime(year, 12, 31);
        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    }
}
