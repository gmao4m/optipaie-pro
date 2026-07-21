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
    /// Attendance module orchestration. Owns ALL attendance business rules so every
    /// screen and the payroll integration agree:
    ///   • one record per employee per day (upsert, never duplicated);
    ///   • worked hours  = check-out − check-in;
    ///   • late minutes  = arrival beyond (standard start + tolerance);
    ///   • overtime      = worked hours beyond the standard day;
    ///   • non-worked statuses (absent/leave/holiday/rest) zero the derived values.
    /// Reads employees from the SHARED repository — attendance never copies employee data.
    /// </summary>
    public sealed class AttendanceService : IAttendanceService
    {
        private const string KeyStart = "Attendance.StandardStart";
        private const string KeyHours = "Attendance.StandardHours";
        private const string KeyTolerance = "Attendance.LateToleranceMinutes";

        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public AttendanceService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        public Result<long> Save(AttendanceRecord record)
        {
            if (record == null)
            {
                return Result.Fail<long>("Aucune donnée de pointage.", "Attendance_Required");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                AttendanceSettings settings = ReadSettings(uow);
                Result validation = Validate(uow, record);
                if (validation.IsFailure)
                {
                    return Result.Fail<long>(validation.Error, validation.ErrorCode);
                }

                Apply(record, settings);

                AttendanceRecord existing = uow.Attendance.GetByEmployeeAndDate(record.EmployeeId, record.WorkDate);
                if (existing == null)
                {
                    return Result.Ok(uow.Attendance.Insert(record));
                }

                record.Id = existing.Id;
                record.CreatedAtUtc = existing.CreatedAtUtc;
                uow.Attendance.Update(record);
                return Result.Ok(record.Id);
            }
        }

        public Result SaveMany(IEnumerable<AttendanceRecord> records)
        {
            List<AttendanceRecord> list = (records ?? Enumerable.Empty<AttendanceRecord>()).ToList();
            if (list.Count == 0)
            {
                return Result.Ok();
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                AttendanceSettings settings = ReadSettings(uow);

                foreach (AttendanceRecord record in list)
                {
                    Result validation = Validate(uow, record);
                    if (validation.IsFailure)
                    {
                        return validation;
                    }
                }

                uow.BeginTransaction();
                try
                {
                    foreach (AttendanceRecord record in list)
                    {
                        Apply(record, settings);
                        AttendanceRecord existing = uow.Attendance.GetByEmployeeAndDate(record.EmployeeId, record.WorkDate);
                        if (existing == null)
                        {
                            uow.Attendance.Insert(record);
                        }
                        else
                        {
                            record.Id = existing.Id;
                            record.CreatedAtUtc = existing.CreatedAtUtc;
                            uow.Attendance.Update(record);
                        }
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
                uow.Attendance.SoftDelete(id);
                return Result.Ok();
            }
        }

        public AttendanceRecord Get(long employeeId, DateTime workDate)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Attendance.GetByEmployeeAndDate(employeeId, workDate);
            }
        }

        public IReadOnlyList<AttendanceRecord> GetCompanyDay(long companyId, DateTime workDate)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Attendance.GetByCompanyRange(companyId, workDate.Date, workDate.Date).ToList();
            }
        }

        public AttendanceSummary GetMonthlySummary(long employeeId, int year, int month)
        {
            DateTime from = FirstDay(year, month);
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                List<AttendanceRecord> records =
                    uow.Attendance.GetByEmployeeRange(employeeId, from, LastDay(from)).ToList();
                AttendanceSummary summary = Summarise(records, year, month);
                summary.EmployeeId = employeeId;
                return summary;
            }
        }

        public IReadOnlyList<AttendanceSummary> GetCompanyMonthlySummary(long companyId, int year, int month)
        {
            DateTime from = FirstDay(year, month);
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                List<AttendanceRecord> records =
                    uow.Attendance.GetByCompanyRange(companyId, from, LastDay(from)).ToList();

                var byEmployee = records.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
                var result = new List<AttendanceSummary>();

                foreach (Employee employee in uow.Employees.GetByCompany(companyId))
                {
                    List<AttendanceRecord> forEmployee = byEmployee.TryGetValue(employee.Id, out var rows)
                        ? rows
                        : new List<AttendanceRecord>();

                    AttendanceSummary summary = Summarise(forEmployee, year, month);
                    summary.EmployeeId = employee.Id;
                    summary.EmployeeName = (employee.LastNameFr + " " + employee.FirstNameFr).Trim();
                    result.Add(summary);
                }

                return result;
            }
        }

        public AttendanceSettings GetSettings()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return ReadSettings(uow);
            }
        }

        public Result SaveSettings(AttendanceSettings settings)
        {
            if (settings == null)
            {
                return Result.Fail("Paramètres manquants.", "Attendance_SettingsRequired");
            }

            if (!TryParseTime(settings.StandardStart, out _))
            {
                return Result.Fail("Heure de début invalide (format HH:mm).", "Attendance_StartInvalid");
            }

            if (settings.StandardHours <= 0m || settings.StandardHours > 24m)
            {
                return Result.Fail("Nombre d'heures standard invalide.", "Attendance_HoursInvalid");
            }

            if (settings.LateToleranceMinutes < 0 || settings.LateToleranceMinutes > 240)
            {
                return Result.Fail("Tolérance de retard invalide.", "Attendance_ToleranceInvalid");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.AppSettings.Upsert(KeyStart, settings.StandardStart);
                uow.AppSettings.Upsert(KeyHours, settings.StandardHours.ToString(CultureInfo.InvariantCulture));
                uow.AppSettings.Upsert(KeyTolerance, settings.LateToleranceMinutes.ToString(CultureInfo.InvariantCulture));
                return Result.Ok();
            }
        }

        // -- internals ---------------------------------------------------------

        private static Result Validate(IUnitOfWork uow, AttendanceRecord record)
        {
            if (record == null)
            {
                return Result.Fail("Aucune donnée de pointage.", "Attendance_Required");
            }

            if (record.EmployeeId <= 0 || !uow.Employees.ExistsById(record.EmployeeId))
            {
                return Result.Fail("Employé introuvable.", "Attendance_EmployeeNotFound");
            }

            if (record.WorkDate == default(DateTime))
            {
                return Result.Fail("La date est obligatoire.", "Attendance_DateRequired");
            }

            if (record.WorkDate.Date > DateTime.Today)
            {
                return Result.Fail("La date de pointage ne peut pas être dans le futur.", "Attendance_FutureDate");
            }

            bool worked = record.Status == AttendanceStatus.Present || record.Status == AttendanceStatus.Late;

            if (!string.IsNullOrWhiteSpace(record.CheckIn) && !TryParseTime(record.CheckIn, out _))
            {
                return Result.Fail("Heure d'arrivée invalide (format HH:mm).", "Attendance_CheckInInvalid");
            }

            if (!string.IsNullOrWhiteSpace(record.CheckOut) && !TryParseTime(record.CheckOut, out _))
            {
                return Result.Fail("Heure de sortie invalide (format HH:mm).", "Attendance_CheckOutInvalid");
            }

            if (worked && string.IsNullOrWhiteSpace(record.CheckIn))
            {
                return Result.Fail("L'heure d'arrivée est obligatoire pour un jour travaillé.", "Attendance_CheckInRequired");
            }

            if (TryParseTime(record.CheckIn, out TimeSpan inTime) &&
                TryParseTime(record.CheckOut, out TimeSpan outTime) &&
                outTime < inTime)
            {
                return Result.Fail("L'heure de sortie doit suivre l'heure d'arrivée.", "Attendance_CheckOutBeforeCheckIn");
            }

            return Result.Ok();
        }

        /// <summary>Recomputes the derived values and normalises the status.</summary>
        private static void Apply(AttendanceRecord record, AttendanceSettings settings)
        {
            record.WorkDate = record.WorkDate.Date;

            bool worked = record.Status == AttendanceStatus.Present || record.Status == AttendanceStatus.Late;
            if (!worked)
            {
                record.CheckIn = null;
                record.CheckOut = null;
                record.WorkedHours = 0m;
                record.LateMinutes = 0;
                record.OvertimeHours = 0m;
                return;
            }

            decimal hours = 0m;
            if (TryParseTime(record.CheckIn, out TimeSpan inTime) &&
                TryParseTime(record.CheckOut, out TimeSpan outTime) && outTime > inTime)
            {
                hours = Math.Round((decimal)(outTime - inTime).TotalHours, 2, MidpointRounding.AwayFromZero);
            }

            record.WorkedHours = hours;
            record.OvertimeHours = hours > settings.StandardHours
                ? Math.Round(hours - settings.StandardHours, 2, MidpointRounding.AwayFromZero)
                : 0m;

            int late = 0;
            if (TryParseTime(record.CheckIn, out TimeSpan arrival) &&
                TryParseTime(settings.StandardStart, out TimeSpan start))
            {
                double minutes = (arrival - start).TotalMinutes - settings.LateToleranceMinutes;
                if (minutes > 0) late = (int)Math.Ceiling(minutes);
            }

            record.LateMinutes = late;

            // A worked day with lateness is reported as "Retard" — one consistent rule.
            record.Status = late > 0 ? AttendanceStatus.Late : AttendanceStatus.Present;
        }

        private static AttendanceSummary Summarise(IEnumerable<AttendanceRecord> records, int year, int month)
        {
            var summary = new AttendanceSummary { Year = year, Month = month };

            foreach (AttendanceRecord r in records)
            {
                summary.RecordedDays++;
                summary.WorkedHours += r.WorkedHours;
                summary.OvertimeHours += r.OvertimeHours;
                summary.LateMinutes += r.LateMinutes;
                if (r.LateMinutes > 0) summary.LateCount++;

                switch (r.Status)
                {
                    case AttendanceStatus.Present: summary.PresentDays++; break;
                    case AttendanceStatus.Late: summary.PresentDays++; break;
                    case AttendanceStatus.Absent: summary.AbsentDays++; break;
                    case AttendanceStatus.Leave: summary.LeaveDays++; break;
                    case AttendanceStatus.Holiday: summary.HolidayDays++; break;
                    case AttendanceStatus.Rest: summary.RestDays++; break;
                }
            }

            return summary;
        }

        private static AttendanceSettings ReadSettings(IUnitOfWork uow)
        {
            var settings = new AttendanceSettings();

            AppSetting start = uow.AppSettings.Get(KeyStart);
            if (start != null && TryParseTime(start.SettingValue, out _))
            {
                settings.StandardStart = start.SettingValue;
            }

            AppSetting hours = uow.AppSettings.Get(KeyHours);
            if (hours != null && decimal.TryParse(hours.SettingValue, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out decimal h) && h > 0m && h <= 24m)
            {
                settings.StandardHours = h;
            }

            AppSetting tolerance = uow.AppSettings.Get(KeyTolerance);
            if (tolerance != null && int.TryParse(tolerance.SettingValue, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int t) && t >= 0)
            {
                settings.LateToleranceMinutes = t;
            }

            return settings;
        }

        private static bool TryParseTime(string value, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(value)) return false;
            return TimeSpan.TryParseExact(value.Trim(), new[] { @"hh\:mm", @"h\:mm" },
                CultureInfo.InvariantCulture, out time);
        }

        private static DateTime FirstDay(int year, int month) => new DateTime(year, month, 1);
        private static DateTime LastDay(DateTime firstDay) => firstDay.AddMonths(1).AddDays(-1);
    }
}
