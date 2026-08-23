using System;
using System.Collections.Generic;

namespace OptiPaie.Core.Certificates
{
    // ─────────────────────────────────────────────────────────────────────────
    // ATS/DRT certificate domain — ported VERBATIM from the source ATS/DRT tool
    // (AtsDrt.Domain). These are document-scoped snapshot models: an OptiPaie
    // employee/company is mapped onto them just before rendering, so the official
    // calculation + bookmark logic stays byte-for-byte identical to the original.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A registered employer (company or branch) as it appears on the certificate.
    /// </summary>
    public class Company
    {
        public int Id { get; set; }
        public string ManagerName { get; set; }        // Nom et Prénom du responsable
        public string EmployerNumber { get; set; }      // N° Adhérent CNAS (10 digits, stored raw)
        public string CompanyName { get; set; }         // Raison sociale
        public string Address { get; set; }
        public string Location { get; set; }            // "Lieu" — city used on the certificate signature line

        /// <summary>Formats the 10-digit employer number as "XX XXX XXX XX".</summary>
        public string FormattedEmployerNumber => FormattingHelpers.FormatEmployerNumber(EmployerNumber);
    }

    public class Employee
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }              // FK -> Company

        public string LastName { get; set; }
        public string FirstName { get; set; }
        public DateTime? BirthDate { get; set; }
        public string BirthPlace { get; set; }
        public string SocialSecurityNumber { get; set; } // 12 digits, stored raw
        public string Address { get; set; }
        public DateTime? HireDate { get; set; }
        public string Position { get; set; }

        public string FullName => $"{LastName} {FirstName}".Trim();

        /// <summary>Formats the 12-digit NSS as "XX XXXX XXXX XX".</summary>
        public string FormattedNss => FormattingHelpers.FormatNss(SocialSecurityNumber);
    }

    /// <summary>
    /// One work-stoppage record (sick leave / arrêt de travail) for an employee.
    /// Each generates its own ATS/DRT certificate.
    /// </summary>
    public class WorkStoppage
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public DateTime StoppageDate { get; set; }       // Date arrêt de travail (as recorded)
        public int NumberOfDays { get; set; }             // Nombre de jours
    }

    /// <summary>
    /// One line of the 12-month contribution grid shown on the ATS certificate.
    /// </summary>
    public class MonthlyContribution
    {
        public string MonthLabel { get; set; }            // "Mois et année de référence"

        /// <summary>"Nombre de jours travaillés" column — a DAY count, never hours.
        /// (The old field carried hours here, which the CNAS form's day column rejects.)</summary>
        public decimal? DaysWorked { get; set; }

        /// <summary>"Motif absences" column — a free-text reason (e.g. "Maladie", "Congé"),
        /// never a day count. Blank when there were no absences.</summary>
        public string AbsenceReason { get; set; }

        public decimal? ContributionBase { get; set; }     // "Salaire soumis à cotisations"

        /// <summary>
        /// True for months within the selected range (editable in the UI). False for
        /// the unused trailing slots — these print as "/" on the certificate, matching
        /// the original tool's behaviour exactly.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>Employee's CNAS share = 9% of the contribution base, rounded to 2 decimals.</summary>
        public decimal? EmployeeShare => ContributionBase.HasValue
            ? Math.Round(ContributionBase.Value * 0.09m, 2)
            : (decimal?)null;
    }

    public enum CertificateType
    {
        Ats,   // Attestation de Travail et de Salaire
        Drt    // Déclaration de Reprise ou de Non Reprise de Travail
    }

    public static class FormattingHelpers
    {
        /// <summary>Digits of the NSS (social security number) — exactly this many.</summary>
        public const int NssDigits = 12;

        /// <summary>Digits of the CNAS employer/adherent number — exactly this many.</summary>
        public const int EmployerNumberDigits = 10;

        /// <summary>
        /// True when <paramref name="raw"/> is exactly <paramref name="length"/> digits.
        /// Spaces are tolerated (people type "88 0412 1234 56"), anything else — letters,
        /// dashes, wrong length — is rejected. These numbers are printed onto an official
        /// CNAS certificate, so a malformed value must never be accepted silently.
        /// </summary>
        public static bool IsDigitsOfLength(string raw, int length)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var s = raw.Replace(" ", "").Trim();
            if (s.Length != length) return false;
            foreach (var c in s)
                if (!char.IsDigit(c)) return false;
            return true;
        }

        /// <summary>Strips spaces so the value is stored as raw digits.</summary>
        public static string NormalizeDigits(string raw) =>
            string.IsNullOrEmpty(raw) ? raw : raw.Replace(" ", "").Trim();

        public static bool IsValidNss(string raw) => IsDigitsOfLength(raw, NssDigits);
        public static bool IsValidEmployerNumber(string raw) => IsDigitsOfLength(raw, EmployerNumberDigits);

        public static string FormatEmployerNumber(string raw)
        {
            var digits = OnlyDigits(raw);
            if (digits.Length != 10) return raw;
            return $"{digits.Substring(0, 2)} {digits.Substring(2, 3)} {digits.Substring(5, 3)} {digits.Substring(8, 2)}";
        }

        public static string FormatNss(string raw)
        {
            var digits = OnlyDigits(raw);
            if (digits.Length != 12) return raw;
            return $"{digits.Substring(0, 2)} {digits.Substring(2, 4)} {digits.Substring(6, 4)} {digits.Substring(10, 2)}";
        }

        private static string OnlyDigits(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var chars = new List<char>();
            foreach (var c in value)
                if (char.IsDigit(c)) chars.Add(c);
            return new string(chars.ToArray());
        }
    }
}
