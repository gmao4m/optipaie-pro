using System;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A legal public holiday (jour férié chômé et payé). <see cref="CompanyId"/> null = national
    /// (applies to every company); otherwise company-specific. A holiday that falls inside a leave
    /// period is not counted against the balance. Religious holidays move each year, so they are
    /// entered per year (<see cref="IsReligious"/> flags them for the yearly-entry screen).
    /// </summary>
    public sealed class Holiday : EntityBase
    {
        public long? CompanyId { get; set; }
        public DateTime HolidayDate { get; set; }
        public string NameAr { get; set; }
        public bool IsReligious { get; set; }
        public bool IsDeleted { get; set; }
    }
}
