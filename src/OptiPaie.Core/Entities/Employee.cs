using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// An employee who belongs to exactly one <see cref="Company"/>.
    /// Identity is bilingual for payslip printing.
    /// </summary>
    public sealed class Employee : EntityBase
    {
        /// <summary>Foreign key to the owning company.</summary>
        public long CompanyId { get; set; }

        /// <summary>Last name in French.</summary>
        public string LastNameFr { get; set; }

        /// <summary>Last name in Arabic.</summary>
        public string LastNameAr { get; set; }

        /// <summary>First name in French.</summary>
        public string FirstNameFr { get; set; }

        /// <summary>First name in Arabic.</summary>
        public string FirstNameAr { get; set; }

        /// <summary>Gender (for correct honorifics on the payslip).</summary>
        public Gender Gender { get; set; }

        /// <summary>Numéro de sécurité sociale (NSS).</summary>
        public string Nss { get; set; }

        /// <summary>National identity number.</summary>
        public string NationalId { get; set; }

        /// <summary>Date of birth. Null if unknown.</summary>
        public DateTime? BirthDate { get; set; }

        /// <summary>Place of birth (lieu de naissance) — used on the ATS/DRT attestations. May be null.</summary>
        public string BirthPlace { get; set; }

        /// <summary>Home address — used on the ATS attestation (SADRESSE). May be null.</summary>
        public string Address { get; set; }

        /// <summary>Hire date (date de recrutement).</summary>
        public DateTime HireDate { get; set; }

        /// <summary>Exit date (date de sortie). Null while employed.</summary>
        public DateTime? ExitDate { get; set; }

        /// <summary>Professional category / classification.</summary>
        public string Category { get; set; }

        /// <summary>Department / service the employee belongs to (used by the Attendance matrix).</summary>
        public string Department { get; set; }

        /// <summary>Job position (poste).</summary>
        public string Poste { get; set; }

        /// <summary>Contract type.</summary>
        public ContractType ContractType { get; set; }

        /// <summary>Marital status.</summary>
        public MaritalStatus MaritalStatus { get; set; }

        /// <summary>Number of dependents.</summary>
        public int Dependents { get; set; }

        /// <summary>Monthly base salary (salaire de base).</summary>
        public decimal BaseSalary { get; set; }

        /// <summary>How the net salary is paid.</summary>
        public PaymentMode PaymentMode { get; set; }

        /// <summary>Bank account / RIB for transfers. May be null.</summary>
        public string Rib { get; set; }

        /// <summary>Whether the employee is currently active (eligible for payroll).</summary>
        public bool IsActive { get; set; }

        /// <summary>UTC timestamp of the last update. Null if never updated.</summary>
        public DateTime? UpdatedAtUtc { get; set; }

        /// <summary>Soft-delete flag; deleted employees never orphan payroll history.</summary>
        public bool IsDeleted { get; set; }

        /// <summary>
        /// Optimistic-concurrency token. Incremented by the database on every update; an update
        /// only succeeds when the stored value still equals the one the editor loaded, so a second
        /// user who saved first is never silently overwritten. Default 0 for existing rows.
        /// </summary>
        public long RowVersion { get; set; }

        // ----- Per-employee weekly work pattern (overrides the company calendar) ------------------
        //
        // The default is "follows the company": UsesOwnSchedule = false, so the company work
        // calendar decides which weekdays this person works. Turn it on and the seven day flags
        // below define his own week — e.g. a guard who works Friday and Saturday. Nothing changes
        // for an employee until UsesOwnSchedule is set.

        /// <summary>When true, this employee's own weekly pattern (below) overrides the company calendar.</summary>
        public bool UsesOwnSchedule { get; set; }

        public bool SchedMon { get; set; } = true;
        public bool SchedTue { get; set; } = true;
        public bool SchedWed { get; set; } = true;
        public bool SchedThu { get; set; } = true;
        public bool SchedFri { get; set; }
        public bool SchedSat { get; set; }
        public bool SchedSun { get; set; } = true;

        /// <summary>
        /// Whether this employee is expected to work on the given weekday. Uses his own pattern when
        /// <see cref="UsesOwnSchedule"/> is set, otherwise falls back to the company calendar. This is
        /// the single answer the daily list, the workable-days maths and the reports all rely on, so a
        /// guard whose own pattern says he works Friday is expected, counted and paid for that Friday.
        /// </summary>
        public bool WorksOn(DayOfWeek day, CompanyWorkCalendar companyFallback)
        {
            if (UsesOwnSchedule)
            {
                switch (day)
                {
                    case DayOfWeek.Monday: return SchedMon;
                    case DayOfWeek.Tuesday: return SchedTue;
                    case DayOfWeek.Wednesday: return SchedWed;
                    case DayOfWeek.Thursday: return SchedThu;
                    case DayOfWeek.Friday: return SchedFri;
                    case DayOfWeek.Saturday: return SchedSat;
                    default: return SchedSun;
                }
            }

            return companyFallback != null && companyFallback.IsWorkDay(day);
        }
    }
}
