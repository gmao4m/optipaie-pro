using System;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A company / institution (the employer). Parent of <see cref="Employee"/>.
    /// Legal identity is bilingual so it can be printed on Arabic or French payslips.
    /// </summary>
    public sealed class Company : EntityBase
    {
        /// <summary>Legal name in French.</summary>
        public string NameFr { get; set; }

        /// <summary>Legal name in Arabic.</summary>
        public string NameAr { get; set; }

        /// <summary>Legal form (SARL, EURL, EPE, EPIC, ...).</summary>
        public string LegalForm { get; set; }

        /// <summary>Address in French.</summary>
        public string AddressFr { get; set; }

        /// <summary>Address in Arabic.</summary>
        public string AddressAr { get; set; }

        /// <summary>Numéro d'Identification Fiscale.</summary>
        public string Nif { get; set; }

        /// <summary>Numéro d'Identification Statistique.</summary>
        public string Nis { get; set; }

        /// <summary>Registre de Commerce number.</summary>
        public string Rc { get; set; }

        /// <summary>Article d'imposition.</summary>
        public string ArticleImposition { get; set; }

        /// <summary>Employer CNAS affiliation number.</summary>
        public string CnasEmployerNumber { get; set; }

        /// <summary>CACOBATPH affiliation number (construction/public works sector).</summary>
        public string Cacobatph { get; set; }

        /// <summary>True if the company operates in the BTPH (bâtiment/travaux publics/hydraulique) sector.</summary>
        public bool BtphSector { get; set; }

        /// <summary>
        /// True to apply the optional CACOBATPH contributions on payslips and expose the
        /// CACOBATPH declarations. Off by default and only meaningful when <see cref="BtphSector"/>
        /// is on; never alters the payroll engine.
        /// </summary>
        public bool CacobatphEnabled { get; set; }

        /// <summary>Bank name.</summary>
        public string Bank { get; set; }

        /// <summary>Bank account / RIB.</summary>
        public string BankAccount { get; set; }

        /// <summary>Currency code (default DZD).</summary>
        public string Currency { get; set; }

        /// <summary>Contact phone number.</summary>
        public string Phone { get; set; }

        /// <summary>Contact e-mail.</summary>
        public string Email { get; set; }

        /// <summary>Company logo image bytes, printed on the payslip header. May be null.</summary>
        public byte[] Logo { get; set; }

        /// <summary>UTC timestamp of the last update. Null if never updated.</summary>
        public DateTime? UpdatedAtUtc { get; set; }

        /// <summary>Soft-delete flag; deleted companies are hidden but never orphan payroll history.</summary>
        public bool IsDeleted { get; set; }
    }
}
