using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using OptiPaie.Core.Enums;
using OptiPaie.Desktop.Common;

namespace OptiPaie.Desktop.Converters
{
    /// <summary>Renders a domain enum value as its French label.</summary>
    public sealed class EnumToFrenchConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value)
            {
                case Gender g: return EnumLabels.GenderLabel(g);
                case ContractType c: return EnumLabels.ContractLabel(c);
                case MaritalStatus m: return EnumLabels.MaritalLabel(m);
                case PaymentMode p: return EnumLabels.PaymentLabel(p);
                default: return value?.ToString() ?? string.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>byte[] logo → a WPF ImageSource (for company logos), or null.</summary>
    public sealed class BytesToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is byte[] bytes) || bytes.Length == 0)
            {
                return null;
            }

            try
            {
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.StreamSource = new System.IO.MemoryStream(bytes);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>ElementType → "Gain" / "Retenue".</summary>
    public sealed class ElementNatureConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool deduction = value is OptiPaie.Core.Enums.ElementType t && t == OptiPaie.Core.Enums.ElementType.Deduction;
            return OptiPaie.Desktop.Localization.TranslationSource.Instance[deduction ? "Payroll_Col_Deduction" : "Payroll_Col_Gain"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Employee → two-letter initials (last + first) for the avatar badge.</summary>
    public sealed class InitialsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is OptiPaie.Core.Entities.Employee e)
            {
                char a = !string.IsNullOrWhiteSpace(e.LastNameFr) ? char.ToUpperInvariant(e.LastNameFr.Trim()[0]) : ' ';
                char b = !string.IsNullOrWhiteSpace(e.FirstNameFr) ? char.ToUpperInvariant(e.FirstNameFr.Trim()[0]) : ' ';
                return (a.ToString() + b).Trim();
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>true → SemiBold, false → Normal (highlights the base-salary row).</summary>
    public sealed class BoolToFontWeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>true (gain) → green, false (deduction) → red.</summary>
    public sealed class GainToBrushConverter : IValueConverter
    {
        private static readonly System.Windows.Media.Brush Gain =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x13, 0x7B, 0x50));
        private static readonly System.Windows.Media.Brush Deduction =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x45, 0x3B));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool b && b) ? Gain : Deduction;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Empty/whitespace string → Visible (for placeholder text), otherwise Collapsed.</summary>
    public sealed class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Non-empty string → Visible, otherwise Collapsed (for optional badges).</summary>
    public sealed class NonEmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>NotificationSeverity → a status colour dot (Urgent red, Warning amber, Info gray).</summary>
    public sealed class SeverityToBrushConverter : IValueConverter
    {
        private static readonly System.Windows.Media.Brush Urgent =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x45, 0x3B));
        private static readonly System.Windows.Media.Brush Warning =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC9, 0x7A, 0x10));
        private static readonly System.Windows.Media.Brush Info =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A, 0x94, 0xA2));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is OptiPaie.Core.Dtos.NotificationSeverity s)
            {
                switch (s)
                {
                    case OptiPaie.Core.Dtos.NotificationSeverity.Urgent: return Urgent;
                    case OptiPaie.Core.Dtos.NotificationSeverity.Warning: return Warning;
                }
            }

            return Info;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>true → Collapsed, false → Visible (for empty-state text).</summary>
    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool v && v;
            return b ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility vis && vis != Visibility.Visible;
        }
    }

    /// <summary>true → Visible, false → Collapsed.</summary>
    public sealed class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool v && v;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility vis && vis == Visibility.Visible;
        }
    }

    /// <summary>
    /// Two-way converter for EVERY editable numeric field (decimal / decimal? / double / double? / int / int?).
    /// Input side accepts a comma OR a dot as the decimal separator, plus grouping spaces, whatever the
    /// Windows locale — "1250,50" and "1250.50" both become 1250.5. A partially-typed / unparseable string
    /// returns <see cref="Binding.DoNothing"/> so the keystroke is never rejected mid-typing; a blank value
    /// clears a nullable to null (or a non-nullable to 0). Display uses the UI culture for consistency.
    /// </summary>
    public sealed class FlexibleDecimalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            switch (value)
            {
                case decimal d: return d.ToString(culture);
                case double db: return db.ToString(culture);
                case float f: return f.ToString(culture);
                case int i: return i.ToString(culture);
                case long l: return l.ToString(culture);
                default: return value.ToString();
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = value as string;
            Type t = Nullable.GetUnderlyingType(targetType) ?? targetType;
            bool nullable = Nullable.GetUnderlyingType(targetType) != null || !targetType.IsValueType;

            if (string.IsNullOrWhiteSpace(s))
                return nullable ? null : Zero(t);

            if (!OptiPaie.Common.Text.FlexibleNumber.TryParse(s, out decimal dec))
                return Binding.DoNothing; // mid-typing / garbage → keep current value, no validation error

            try
            {
                if (t == typeof(decimal)) return dec;
                if (t == typeof(double)) return (double)dec;
                if (t == typeof(float)) return (float)dec;
                if (t == typeof(int)) return decimal.ToInt32(decimal.Truncate(dec));
                if (t == typeof(long)) return decimal.ToInt64(decimal.Truncate(dec));
                return System.Convert.ChangeType(dec, t, CultureInfo.InvariantCulture);
            }
            catch
            {
                return Binding.DoNothing;
            }
        }

        private static object Zero(Type t)
        {
            if (t == typeof(decimal)) return 0m;
            if (t == typeof(double)) return 0d;
            if (t == typeof(float)) return 0f;
            if (t == typeof(int)) return 0;
            if (t == typeof(long)) return 0L;
            return Activator.CreateInstance(t);
        }
    }
}
