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

    /// <summary>French labels for the domain enums (self-contained; no external localizer).</summary>
    public static class EnumLabels
    {
        public static string GenderLabel(Gender g) => g == Gender.Female ? "Féminin" : "Masculin";

        public static string ContractLabel(ContractType c)
        {
            switch (c)
            {
                case ContractType.Cdi: return "CDI";
                case ContractType.Cdd: return "CDD";
                case ContractType.Apprenticeship: return "Apprentissage";
                case ContractType.Internship: return "Pré-emploi / Stage";
                default: return "Autre";
            }
        }

        public static string MaritalLabel(MaritalStatus m)
        {
            switch (m)
            {
                case MaritalStatus.Single: return "Célibataire";
                case MaritalStatus.Married: return "Marié(e)";
                case MaritalStatus.Divorced: return "Divorcé(e)";
                default: return "Veuf/Veuve";
            }
        }

        public static string PaymentLabel(PaymentMode p)
        {
            switch (p)
            {
                case PaymentMode.BankTransfer: return "Virement bancaire";
                case PaymentMode.Cash: return "Espèces";
                default: return "Chèque";
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
