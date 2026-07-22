using System.Collections.Generic;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// A rendered report: a title, column headers and string rows. Every report in the
    /// Reports Center produces this one shape, so the preview grid, the PDF and the CSV
    /// export all work uniformly.
    /// </summary>
    public sealed class ReportTable
    {
        public string Title { get; set; } = string.Empty;

        /// <summary>Sub-title (period / company), shown under the title.</summary>
        public string Subtitle { get; set; } = string.Empty;

        public IReadOnlyList<string> Columns { get; set; } = new List<string>();

        public IReadOnlyList<IReadOnlyList<string>> Rows { get; set; } = new List<IReadOnlyList<string>>();

        /// <summary>Column indexes to right-align (numbers/money). Empty = all left.</summary>
        public IReadOnlyList<int> NumericColumns { get; set; } = new List<int>();
    }

    /// <summary>One entry in the report library.</summary>
    public sealed class ReportDescriptor
    {
        public ReportDescriptor(string key, string title, string category, bool needsMonth)
        {
            Key = key;
            Title = title;
            Category = category;
            NeedsMonth = needsMonth;
        }

        public string Key { get; }
        public string Title { get; }
        public string Category { get; }

        /// <summary>True when the report is month-scoped (needs a month, not just a year).</summary>
        public bool NeedsMonth { get; }
    }
}
