using System;
using System.Globalization;
using System.Windows.Data;
using OptiPaie.Core.Security;

namespace OptiPaie.Desktop.Converters
{
    /// <summary>
    /// Masks a salary figure bound directly in XAML (e.g. an employee grid cell) when the signed-in
    /// role may not see pay — the SAME <see cref="Permission.ViewSalary"/> gate every other surface
    /// uses. Reads the app-wide authorization context, so any <c>{Binding Amount, Converter=SalaryMask}</c>
    /// is gated without a per-row view model. The optional parameter is a .NET numeric format
    /// (default "N2"); " DA" is appended to a visible amount to match the existing cells.
    /// </summary>
    public sealed class SalaryMaskConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is decimal amount))
            {
                try { amount = System.Convert.ToDecimal(value, culture); }
                catch { return value; }
            }

            string format = parameter as string ?? "N2";
            IAccessContext access = App.Services != null ? App.Services.Authorization : null;
            if (!SalaryVisibility.CanSee(access))
            {
                return SalaryVisibility.Masked;
            }

            return amount.ToString(format, culture) + " DA";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
