using System.Collections.Generic;

namespace OptiPaie.Core.Certificates
{
    /// <summary>
    /// Bookmark names below were extracted directly from the real official templates
    /// (ATS_Template.docx, DRT_Template_*.docx) — every name here must match exactly or
    /// the corresponding field is silently left blank on the printed document.
    /// Ported verbatim from the source ATS/DRT tool (AtsDrt.Documents.CertificateBookmarkMapper).
    /// </summary>
    public static class CertificateBookmarkMapper
    {
        public static Dictionary<string, string> MapAts(AtsCertificateData data)
        {
            var values = new Dictionary<string, string>
            {
                ["NMEMPLOYEUR"] = data.Company.ManagerName,
                ["NEMPLOYEUR"] = data.Company.FormattedEmployerNumber,
                ["RS"] = data.Company.CompanyName,
                ["EADRESSE"] = data.Company.Address,
                ["LIEU"] = data.Company.Location,

                ["NMS"] = data.Employee.FullName,
                ["NSS"] = data.Employee.FormattedNss,
                ["NEA"] = data.Employee.BirthPlace,
                ["POSTE"] = data.Employee.Position,
                ["SADRESSE"] = data.Employee.Address,
                ["DATEN"] = FormatDdMmYy(data.Employee.BirthDate),
                ["DATER"] = FormatDdMmYy(data.Employee.HireDate),

                ["DATEAT"] = FormatDdMmYy(data.LastWorkedDate),
                ["AUJOURDHUI"] = FormatDdMmYyyySlash(data.IssueDate),
            };

            // Same "resumed work or not" question the original tool asks on the ATS
            // form itself (Optbtn2OUI/Optbtn2NON in frmCotis).
            if (data.HasResumedWork)
            {
                values["DATEAUJRH"] = "";
                values["DATEREPRISE"] = FormatDdMmYy(data.ResumeDate);
            }
            else
            {
                values["DATEAUJRH"] = FormatDdMmYy(data.IssueDate);
                values["DATEREPRISE"] = "";
            }

            // 12-month table (ATS page 2). Column order on the official form:
            //   M   = Mois et année de référence
            //   JT  = Nombre de jours travaillés   (a DAY count — never hours)
            //   MT  = Motif absences               (free text — never a day count)
            //   SS  = Salaire soumis à cotisations
            //   PO  = Montant de la cotisation (part ouvrière) = 9 % of SS
            // Unused trailing slots print "/", exactly matching the original tool.
            for (int i = 0; i < 12; i++)
            {
                int n = i + 1;
                var row = i < data.Contributions.Count ? data.Contributions[i] : null;
                bool isActive = row?.IsActive == true;

                values[$"M{n}"] = isActive ? row.MonthLabel : "";
                values[$"JT{n}"] = isActive ? (row.DaysWorked?.ToString("0.##") ?? "") : "/";
                values[$"MT{n}"] = isActive ? (row.AbsenceReason ?? "") : "/";
                values[$"SS{n}"] = isActive
                    ? (row.ContributionBase.HasValue ? $"{row.ContributionBase:0.##} DA" : "")
                    : "/";
                values[$"PO{n}"] = isActive
                    ? (row.EmployeeShare.HasValue ? $"{row.EmployeeShare:0.##} DA" : "")
                    : "/";
            }

            return values;
        }

        public static Dictionary<string, string> MapDrt(DrtCertificateData data)
        {
            var values = new Dictionary<string, string>
            {
                ["LIEU"] = data.Company.Location,
                ["NMS"] = data.Employee.LastName,
                ["NMP"] = data.Employee.FirstName,
                ["NSS"] = data.Employee.FormattedNss,
                ["NEA"] = data.Employee.BirthPlace,
                ["DATEN"] = FormatDdMmYy(data.Employee.BirthDate),
                ["DATEAT"] = FormatDdMmYy(data.LastWorkedDate),
                ["AUJOURDHUI"] = FormatDdMmYyyySlash(data.IssueDate),
            };

            if (data.HasResumedWork)
            {
                values["Case1"] = "X";
                values["DATEREPRISE"] = FormatDdMmYy(data.ResumeDate);
            }
            else
            {
                values["Case2"] = "X";
                values["DATEAUJRH"] = FormatDdMmYy(data.IssueDate);

                // "Déclaration sur l'honneur" section — filled only when the employee
                // has not resumed work, matching the original form logic.
                values["NMS2"] = data.Employee.LastName;
                values["NMP2"] = data.Employee.FirstName;
                values["NSS2"] = data.Employee.FormattedNss;
                values["DATEAUJRH2"] = FormatDdMmYy(data.IssueDate);
                values["AUJOURDHUI2"] = FormatDdMmYyyySlash(data.IssueDate);
                values["LIEU1"] = data.Company.Location;
            }

            return values;
        }

        // Always format with InvariantCulture: these strings print onto an official CNAS form, so
        // the digits must stay Latin and the "/" separator must be a plain slash — under an Arabic
        // (ar-DZ) UI the ambient culture injects RTL marks (U+200F) around the separator, which
        // SkiaSharp draws as stray glyphs on the form.
        private static string FormatDdMmYy(System.DateTime? date) =>
            date.HasValue ? date.Value.ToString("ddMMyy", System.Globalization.CultureInfo.InvariantCulture) : "";

        private static string FormatDdMmYyyySlash(System.DateTime? date) =>
            date.HasValue ? date.Value.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) : "";
    }
}
