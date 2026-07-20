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

    /// <summary>The canonical module list, mirroring backend/MODULES.md.</summary>
    public static class Modules
    {
        public sealed class Item
        {
            public Item(string key, string name, bool core) { Key = key; Name = name; Core = core; }
            public string Key { get; }
            public string Name { get; }
            public bool Core { get; }
        }

        public static readonly List<Item> All = new List<Item>
        {
            new Item("payroll", "Paie (core)", true),
            new Item("ats", "ATS / DRT", false),
            new Item("attendance", "Pointage", false),
            new Item("leave", "Congés", false),
            new Item("loans", "Prêts & avances", false),
            new Item("performance", "Évaluation", false),
            new Item("contracts", "Contrats", false),
            new Item("training", "Formation", false),
            new Item("assets", "Biens & équipements", false),
            new Item("work_certificate", "Attestation de travail", false),
        };

        public static string Name(string key)
        {
            Item i = All.Find(x => x.Key == key);
            return i != null ? i.Name : key;
        }
    }
}
