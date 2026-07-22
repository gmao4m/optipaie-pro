using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Services
{
    /// <summary>
    /// The central notification engine. Aggregates time-sensitive alerts from the modules
    /// through their services — contract expiries (escalating urgency), leave awaiting
    /// approval, trainings starting soon — into one ranked list. Read-only; never touches
    /// the payroll engine. New sources are added by extending <see cref="Collect"/>.
    /// </summary>
    public sealed class NotificationService : INotificationService
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly ICompanyService _companies;
        private readonly IEmployeeService _employees;
        private readonly IContractService _contracts;
        private readonly ILeaveService _leave;
        private readonly ITrainingService _training;

        public NotificationService(
            ICompanyService companies,
            IEmployeeService employees,
            IContractService contracts,
            ILeaveService leave,
            ITrainingService training)
        {
            _companies = Guard.AgainstNull(companies, nameof(companies));
            _employees = Guard.AgainstNull(employees, nameof(employees));
            _contracts = Guard.AgainstNull(contracts, nameof(contracts));
            _leave = Guard.AgainstNull(leave, nameof(leave));
            _training = Guard.AgainstNull(training, nameof(training));
        }

        public IReadOnlyList<Notification> GetNotifications(int expiryWindowDays = 30)
        {
            var items = new List<Notification>();
            foreach (Company company in _companies.GetAll())
            {
                Collect(company, expiryWindowDays, items);
            }

            // Most urgent first, then soonest date, then title.
            return items
                .OrderByDescending(n => (int)n.Severity)
                .ThenBy(n => n.Date ?? DateTime.MaxValue)
                .ThenBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
                .Take(50)
                .ToList();
        }

        private void Collect(Company company, int window, List<Notification> items)
        {
            int year = DateTime.Today.Year;
            var names = _employees.GetByCompany(company.Id)
                .ToDictionary(e => e.Id, e => (e.LastNameFr + " " + e.FirstNameFr).Trim());

            // Contract expiries — urgency escalates as the end date nears.
            foreach (ContractSummary c in _contracts.GetExpiring(company.Id, window))
            {
                int days = c.DaysUntilExpiry ?? 0;
                items.Add(new Notification
                {
                    Kind = "contract",
                    Severity = days <= 7 ? NotificationSeverity.Urgent
                             : days <= 15 ? NotificationSeverity.Warning
                             : NotificationSeverity.Info,
                    Title = "Contrat — " + (c.EmployeeName ?? "—"),
                    Detail = "Fin le " + (c.EndDate.HasValue ? c.EndDate.Value.ToString("dd/MM/yyyy", Fr) : "—") +
                             " · " + Countdown(days),
                    ModuleKey = ModuleKeys.Contracts,
                    Date = c.EndDate
                });
            }

            // Leave awaiting approval.
            foreach (LeaveRequest l in _leave.GetByCompanyYear(company.Id, year))
            {
                if (l.Status != LeaveStatus.Pending) continue;
                names.TryGetValue(l.EmployeeId, out string name);
                items.Add(new Notification
                {
                    Kind = "leave",
                    Severity = NotificationSeverity.Warning,
                    Title = "Congé à approuver — " + (name ?? "—"),
                    Detail = l.StartDate.ToString("dd/MM/yyyy", Fr) + " → " + l.EndDate.ToString("dd/MM/yyyy", Fr),
                    ModuleKey = ModuleKeys.Leave,
                    Date = l.StartDate
                });
            }

            // Trainings starting within a week.
            DateTime today = DateTime.Today;
            foreach (TrainingSummary t in _training.GetByCompany(company.Id))
            {
                if (t.Status != TrainingStatus.Planned && t.Status != TrainingStatus.Ongoing) continue;
                int days = (int)(t.StartDate.Date - today).TotalDays;
                if (days < 0 || days > 7) continue;
                items.Add(new Notification
                {
                    Kind = "training",
                    Severity = NotificationSeverity.Info,
                    Title = "Formation — " + (t.Title ?? "—"),
                    Detail = "Débute le " + t.StartDate.ToString("dd/MM/yyyy", Fr) + " · " + Countdown(days),
                    ModuleKey = ModuleKeys.Training,
                    Date = t.StartDate
                });
            }
        }

        private static string Countdown(int days)
        {
            if (days < 0) return "en retard de " + (-days) + " j";
            if (days == 0) return "aujourd'hui";
            if (days == 1) return "demain";
            return "dans " + days + " j";
        }
    }
}
