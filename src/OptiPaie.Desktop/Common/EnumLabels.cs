using System.Collections.Generic;
using OptiPaie.Core.Enums;

namespace OptiPaie.Desktop.Common
{
    /// <summary>A selectable (value, French label) pair for combo boxes.</summary>
    public sealed class EnumOption
    {
        public EnumOption(object value, string label)
        {
            Value = value;
            Label = label;
        }

        public object Value { get; }
        public string Label { get; }

        public override string ToString() => Label;
    }

    /// <summary>
    /// Localized labels for the domain enums. Text is resolved for the active language
    /// through the shared translation source; the French value is the resource fallback,
    /// so nothing breaks if a key is ever missing.
    /// </summary>
    public static class EnumLabels
    {
        private static string L(string key) => OptiPaie.Desktop.Localization.TranslationSource.Instance[key];

        public static string GenderLabel(Gender g) => L(g == Gender.Female ? "Enum_Gender_Female" : "Enum_Gender_Male");

        public static string ContractLabel(ContractType c)
        {
            switch (c)
            {
                case ContractType.Cdi: return L("Enum_Contract_Cdi");
                case ContractType.Cdd: return L("Enum_Contract_Cdd");
                case ContractType.Apprenticeship: return L("Enum_Contract_Apprenticeship");
                case ContractType.Internship: return L("Enum_Contract_Internship");
                default: return L("Enum_Contract_Other");
            }
        }

        public static string MaritalLabel(MaritalStatus m)
        {
            switch (m)
            {
                case MaritalStatus.Single: return L("Enum_Marital_Single");
                case MaritalStatus.Married: return L("Enum_Marital_Married");
                case MaritalStatus.Divorced: return L("Enum_Marital_Divorced");
                default: return L("Enum_Marital_Widowed");
            }
        }

        public static string PaymentLabel(PaymentMode p)
        {
            switch (p)
            {
                case PaymentMode.BankTransfer: return L("Enum_Payment_BankTransfer");
                case PaymentMode.Cash: return L("Enum_Payment_Cash");
                default: return L("Enum_Payment_Cheque");
            }
        }

        public static List<EnumOption> Genders() => new List<EnumOption>
        {
            new EnumOption(Gender.Male, GenderLabel(Gender.Male)),
            new EnumOption(Gender.Female, GenderLabel(Gender.Female))
        };

        public static List<EnumOption> Contracts() => new List<EnumOption>
        {
            new EnumOption(ContractType.Cdi, ContractLabel(ContractType.Cdi)),
            new EnumOption(ContractType.Cdd, ContractLabel(ContractType.Cdd)),
            new EnumOption(ContractType.Apprenticeship, ContractLabel(ContractType.Apprenticeship)),
            new EnumOption(ContractType.Internship, ContractLabel(ContractType.Internship)),
            new EnumOption(ContractType.Other, ContractLabel(ContractType.Other))
        };

        public static List<EnumOption> Maritals() => new List<EnumOption>
        {
            new EnumOption(MaritalStatus.Single, MaritalLabel(MaritalStatus.Single)),
            new EnumOption(MaritalStatus.Married, MaritalLabel(MaritalStatus.Married)),
            new EnumOption(MaritalStatus.Divorced, MaritalLabel(MaritalStatus.Divorced)),
            new EnumOption(MaritalStatus.Widowed, MaritalLabel(MaritalStatus.Widowed))
        };

        public static List<EnumOption> Payments() => new List<EnumOption>
        {
            new EnumOption(PaymentMode.BankTransfer, PaymentLabel(PaymentMode.BankTransfer)),
            new EnumOption(PaymentMode.Cash, PaymentLabel(PaymentMode.Cash)),
            new EnumOption(PaymentMode.Cheque, PaymentLabel(PaymentMode.Cheque))
        };
    }
}
