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
            return (value is OptiPaie.Core.Enums.ElementType t && t == OptiPaie.Core.Enums.ElementType.Deduction) ? "Retenue" : "Gain";
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
}
