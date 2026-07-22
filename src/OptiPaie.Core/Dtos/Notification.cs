using System;

namespace OptiPaie.Core.Dtos
{
    /// <summary>How pressing a notification is (drives colour and ordering).</summary>
    public enum NotificationSeverity
    {
        Info = 0,
        Warning = 1,
        Urgent = 2
    }

    /// <summary>
    /// One alert surfaced by the central notification engine, aggregated from a module.
    /// Read-only; clicking it navigates to <see cref="ModuleKey"/>.
    /// </summary>
    public sealed class Notification
    {
        public string Kind { get; set; }
        public NotificationSeverity Severity { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public string ModuleKey { get; set; }
        public DateTime? Date { get; set; }
    }
}
