using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OptiPaie.Common.Text;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;

namespace OptiPaie.Services.ImportExport
{
    /// <summary>
    /// The single catalogue of importable/exportable employee columns and the exact parse/format
    /// rules for each. Enum columns round-trip through a tolerant parser (French label, Arabic label,
    /// English member name, or the integer value), and export writes the French label. Dates are
    /// ISO (yyyy-MM-dd) on export and accept ISO or dd/MM/yyyy on import; numbers accept comma or dot.
    /// No identifier (NSS, NIF…) has a length rule here — import matches manual entry, nothing stricter.
    /// </summary>
    public static class EmployeeImportColumns
    {
        // -- field keys --
        public const string LastNameFr = "LastNameFr";
        public const string FirstNameFr = "FirstNameFr";
        public const string LastNameAr = "LastNameAr";
        public const string FirstNameAr = "FirstNameAr";
        public const string Gender = "Gender";
        public const string BirthDate = "BirthDate";
        public const string BirthPlace = "BirthPlace";
        public const string NationalId = "NationalId";
        public const string Nss = "Nss";
        public const string Address = "Address";
        public const string HireDate = "HireDate";
        public const string ExitDate = "ExitDate";
        public const string Category = "Category";
        public const string Department = "Department";
        public const string Poste = "Poste";
        public const string Contract = "ContractType";
        public const string Marital = "MaritalStatus";
        public const string Dependents = "Dependents";
        public const string BaseSalary = "BaseSalary";
        public const string Payment = "PaymentMode";
        public const string Rib = "Rib";
        public const string Active = "IsActive";

        public static readonly IReadOnlyList<ImportColumn> All = new List<ImportColumn>
        {
            new ImportColumn(LastNameFr,  "Nom",                    "اللقب",                     true,  "BENALI"),
            new ImportColumn(FirstNameFr, "Prénom",                 "الاسم",                     true,  "Karim"),
            new ImportColumn(LastNameAr,  "Nom (arabe)",            "اللقب بالعربية",            false, "بن علي"),
            new ImportColumn(FirstNameAr, "Prénom (arabe)",         "الاسم بالعربية",            false, "كريم"),
            new ImportColumn(Gender,      "Sexe",                   "الجنس",                     false, "Homme"),
            new ImportColumn(BirthDate,   "Date de naissance",      "تاريخ الميلاد",             false, "1985-03-12"),
            new ImportColumn(BirthPlace,  "Lieu de naissance",      "مكان الميلاد",              false, "Alger"),
            new ImportColumn(NationalId,  "N° pièce d'identité",    "رقم بطاقة التعريف",         false, "109412345678"),
            new ImportColumn(Nss,         "N° sécurité sociale",    "رقم الضمان الاجتماعي",      false, "8512030012345"),
            new ImportColumn(Address,     "Adresse",                "العنوان",                   false, "12 Rue d'Alger"),
            new ImportColumn(HireDate,    "Date de recrutement",    "تاريخ التوظيف",             true,  "2020-01-01"),
            new ImportColumn(ExitDate,    "Date de sortie",         "تاريخ المغادرة",            false, ""),
            new ImportColumn(Category,    "Catégorie",              "الفئة",                     false, "Cadre"),
            new ImportColumn(Department,  "Département",             "القسم",                     false, "Finance"),
            new ImportColumn(Poste,       "Poste",                  "المنصب",                    false, "Comptable"),
            new ImportColumn(Contract,    "Type de contrat",        "نوع العقد",                 false, "CDI"),
            new ImportColumn(Marital,     "Situation familiale",    "الحالة العائلية",           false, "Marié(e)"),
            new ImportColumn(Dependents,  "Personnes à charge",     "عدد المكفولين",             false, "2"),
            new ImportColumn(BaseSalary,  "Salaire de base",        "الأجر القاعدي",             true,  "50000"),
            new ImportColumn(Payment,     "Mode de paiement",       "طريقة الدفع",               false, "Virement"),
            new ImportColumn(Rib,         "RIB",                    "الحساب البنكي (RIB)",       false, ""),
            new ImportColumn(Active,      "Actif (Oui/Non)",        "نشط (نعم/لا)",              false, "Oui"),
        };

        public static ImportColumn ByKey(string key) => All.FirstOrDefault(c => c.Key == key);

        // -- enum round-trip maps: member -> (fr, ar) --
        private static readonly Dictionary<Gender, (string fr, string ar)> Genders = new Dictionary<Gender, (string, string)>
        {
            { Core.Enums.Gender.Male, ("Homme", "ذكر") },
            { Core.Enums.Gender.Female, ("Femme", "أنثى") },
        };
        private static readonly Dictionary<ContractType, (string fr, string ar)> ContractsMap = new Dictionary<ContractType, (string, string)>
        {
            { ContractType.Cdi, ("CDI", "عقد غير محدد المدة") },
            { ContractType.Cdd, ("CDD", "عقد محدد المدة") },
            { ContractType.Apprenticeship, ("Apprentissage", "عقد تمهين") },
            { ContractType.Internship, ("Stage", "تربّص") },
            { ContractType.Other, ("Autre", "أخرى") },
        };
        private static readonly Dictionary<MaritalStatus, (string fr, string ar)> Maritals = new Dictionary<MaritalStatus, (string, string)>
        {
            { MaritalStatus.Single, ("Célibataire", "أعزب") },
            { MaritalStatus.Married, ("Marié(e)", "متزوج") },
            { MaritalStatus.Divorced, ("Divorcé(e)", "مطلّق") },
            { MaritalStatus.Widowed, ("Veuf(ve)", "أرمل") },
        };
        private static readonly Dictionary<PaymentMode, (string fr, string ar)> Payments = new Dictionary<PaymentMode, (string, string)>
        {
            { PaymentMode.BankTransfer, ("Virement", "تحويل بنكي") },
            { PaymentMode.Cash, ("Espèces", "نقدًا") },
            { PaymentMode.Cheque, ("Chèque", "شيك") },
        };

        private static string EnumFr<T>(Dictionary<T, (string fr, string ar)> map, T value) =>
            map.TryGetValue(value, out var t) ? t.fr : value.ToString();

        /// <summary>Tolerant enum parse: integer, English member name, French or Arabic label (trimmed,
        /// case-insensitive). Returns false on an unrecognised non-empty value.</summary>
        private static bool TryEnum<T>(Dictionary<T, (string fr, string ar)> map, string raw, out T value) where T : struct
        {
            value = default(T);
            string s = (raw ?? string.Empty).Trim();
            if (s.Length == 0) return false;

            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) &&
                Enum.IsDefined(typeof(T), n))
            {
                value = (T)(object)n;
                return true;
            }
            if (Enum.TryParse(s, true, out T byName) && Enum.IsDefined(typeof(T), byName))
            {
                value = byName;
                return true;
            }
            foreach (KeyValuePair<T, (string fr, string ar)> kv in map)
            {
                if (string.Equals(kv.Value.fr, s, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(kv.Value.ar, s, StringComparison.Ordinal))
                {
                    value = kv.Key;
                    return true;
                }
            }
            return false;
        }

        // -- export/template cell for a field --
        public static string Cell(Employee e, string key)
        {
            switch (key)
            {
                case LastNameFr: return e.LastNameFr;
                case FirstNameFr: return e.FirstNameFr;
                case LastNameAr: return e.LastNameAr;
                case FirstNameAr: return e.FirstNameAr;
                case Gender: return EnumFr(Genders, e.Gender);
                case BirthDate: return Date(e.BirthDate);
                case BirthPlace: return e.BirthPlace;
                case NationalId: return e.NationalId;
                case Nss: return e.Nss;
                case Address: return e.Address;
                case HireDate: return Date(e.HireDate);
                case ExitDate: return Date(e.ExitDate);
                case Category: return e.Category;
                case Department: return e.Department;
                case Poste: return e.Poste;
                case Contract: return EnumFr(ContractsMap, e.ContractType);
                case Marital: return EnumFr(Maritals, e.MaritalStatus);
                case Dependents: return e.Dependents.ToString(CultureInfo.InvariantCulture);
                case BaseSalary: return e.BaseSalary.ToString("0.##", CultureInfo.InvariantCulture);
                case Payment: return EnumFr(Payments, e.PaymentMode);
                case Rib: return e.Rib;
                case Active: return e.IsActive ? "Oui" : "Non";
                default: return string.Empty;
            }
        }

        private static string Date(DateTime? d) => d.HasValue && d.Value != default(DateTime)
            ? d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;
        private static string Date(DateTime d) => Date((DateTime?)d);

        /// <summary>
        /// Applies one field's raw value onto the employee. Appends a located issue (and leaves the
        /// field at its default) when the value cannot be parsed; an empty optional value is fine.
        /// </summary>
        public static void Apply(Employee e, string key, string raw, int rowNumber, string columnHeader, List<ImportIssue> issues)
        {
            string s = (raw ?? string.Empty).Trim();
            switch (key)
            {
                case LastNameFr: e.LastNameFr = s; break;
                case FirstNameFr: e.FirstNameFr = s; break;
                case LastNameAr: e.LastNameAr = s; break;
                case FirstNameAr: e.FirstNameAr = s; break;
                case BirthPlace: e.BirthPlace = s; break;
                case NationalId: e.NationalId = s; break;
                case Nss: e.Nss = s; break;              // no length rule — matches manual entry
                case Address: e.Address = s; break;
                case Category: e.Category = s; break;
                case Department: e.Department = s; break;
                case Poste: e.Poste = s; break;
                case Rib: e.Rib = s; break;

                case Gender:
                    if (s.Length == 0) { e.Gender = Core.Enums.Gender.Male; }
                    else if (TryEnum(Genders, s, out Gender g)) e.Gender = g;
                    else issues.Add(Bad(rowNumber, columnHeader, s, "Homme / Femme"));
                    break;
                case Contract:
                    if (s.Length == 0) { e.ContractType = ContractType.Cdi; }
                    else if (TryEnum(ContractsMap, s, out ContractType c)) e.ContractType = c;
                    else issues.Add(Bad(rowNumber, columnHeader, s, "CDI / CDD / Stage / Apprentissage / Autre"));
                    break;
                case Marital:
                    if (s.Length == 0) { e.MaritalStatus = MaritalStatus.Single; }
                    else if (TryEnum(Maritals, s, out MaritalStatus m)) e.MaritalStatus = m;
                    else issues.Add(Bad(rowNumber, columnHeader, s, "Célibataire / Marié(e) / Divorcé(e) / Veuf(ve)"));
                    break;
                case Payment:
                    if (s.Length == 0) { e.PaymentMode = PaymentMode.BankTransfer; }
                    else if (TryEnum(Payments, s, out PaymentMode p)) e.PaymentMode = p;
                    else issues.Add(Bad(rowNumber, columnHeader, s, "Virement / Espèces / Chèque"));
                    break;

                case Dependents:
                    if (s.Length == 0) { e.Dependents = 0; }
                    else if (FlexibleNumber.TryParse(s, out decimal dep) && dep >= 0m) e.Dependents = (int)dep;
                    else issues.Add(Bad(rowNumber, columnHeader, s, "nombre entier / عدد صحيح"));
                    break;
                case BaseSalary:
                    if (FlexibleNumber.TryParse(s, out decimal sal)) e.BaseSalary = sal;
                    else issues.Add(Bad(rowNumber, columnHeader, s, "montant / مبلغ"));
                    break;

                case BirthDate: e.BirthDate = ParseDate(s, rowNumber, columnHeader, issues); break;
                case ExitDate: e.ExitDate = ParseDate(s, rowNumber, columnHeader, issues); break;
                case HireDate:
                    DateTime? h = ParseDate(s, rowNumber, columnHeader, issues);
                    if (h.HasValue) e.HireDate = h.Value; // required-ness is checked by the validator
                    break;

                case Active:
                    e.IsActive = ParseBool(s);
                    break;
            }
        }

        private static DateTime? ParseDate(string s, int rowNumber, string columnHeader, List<ImportIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            string[] formats = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd", "dd-MM-yyyy" };
            if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) ||
                DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            {
                return d.Date;
            }
            issues.Add(Bad(rowNumber, columnHeader, s, "AAAA-MM-JJ"));
            return null;
        }

        private static bool ParseBool(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return true; // default active, like the manual form
            s = s.Trim();
            return s.Equals("oui", StringComparison.OrdinalIgnoreCase)
                || s.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || s.Equals("نعم", StringComparison.Ordinal)
                || s.Equals("1", StringComparison.Ordinal)
                || s.Equals("true", StringComparison.OrdinalIgnoreCase)
                || s.Equals("vrai", StringComparison.OrdinalIgnoreCase)
                || s.Equals("actif", StringComparison.OrdinalIgnoreCase);
        }

        private static ImportIssue Bad(int rowNumber, string columnHeader, string value, string expected) =>
            new ImportIssue(rowNumber, columnHeader,
                "Valeur « " + value + " » invalide — attendu : " + expected + " / قيمة غير صالحة");
    }
}
