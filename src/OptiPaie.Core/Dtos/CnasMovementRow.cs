using System;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// One entry/exit movement in a DAC period — a salarié hired (entrée) or who left (sortie)
    /// during the declared month/quarter. Read-only projection of Employee.HireDate/ExitDate,
    /// strictly company-scoped. The DAC movements annex the accountant copies onto the portal.
    /// </summary>
    public sealed class CnasMovementRow
    {
        public CnasMovementRow(long employeeId, string nss, string lastName, string firstName, bool isEntry, DateTime date)
        {
            EmployeeId = employeeId;
            Nss = nss;
            LastName = lastName;
            FirstName = firstName;
            IsEntry = isEntry;
            Date = date;
        }

        public long EmployeeId { get; }
        public string Nss { get; }
        public string LastName { get; }
        public string FirstName { get; }

        /// <summary>True = entrée (embauche dans la période) ; false = sortie (départ dans la période).</summary>
        public bool IsEntry { get; }

        public DateTime Date { get; }
    }
}
