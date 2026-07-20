using System;

namespace OptiPaie.App.Common
{
    /// <summary>A single recently-used item (company, payroll or archive document).</summary>
    public sealed class RecentItem
    {
        public string Kind { get; set; }
        public long Id { get; set; }
        public string Label { get; set; }
        public DateTime WhenUtc { get; set; }
    }
}
