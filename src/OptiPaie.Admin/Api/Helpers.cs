using System;
using System.Collections.Generic;
using System.Globalization;

namespace OptiPaie.Admin.Api
{
    /// <summary>ISO → local display helpers.</summary>
    public static class Dates
    {
        public static string Short(string iso)
        {
            return Parse(iso, out DateTime d) ? d.ToLocalTime().ToString("dd/MM/yyyy") : "—";
        }

        public static string DateTime(string iso)
        {
            return Parse(iso, out DateTime d) ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "—";
        }

        private static bool Parse(string iso, out DateTime value)
        {
            return System.DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out value);
        }
    }
}
