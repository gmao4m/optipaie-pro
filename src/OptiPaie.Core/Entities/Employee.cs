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

        /// <summary>Hire date (date de recrutement).</summary>
        public DateTime HireDate { get; set; }

        /// <summary>Exit date (date de sortie). Null while employed.</summary>
        public DateTime? ExitDate { get; set; }

        /// <summary>Professional category / classification.</summary>
        public string Category { get; set; }

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
    }
}
