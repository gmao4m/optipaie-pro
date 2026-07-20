using System;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// A UI/application preference stored as a key/value pair (e.g. language, theme,
    /// backup path, default company). Distinct from <see cref="LegalParameter"/>,
    /// which holds payroll legal values. The key is the natural primary key.
    /// </summary>
    public sealed class AppSetting
    {
        /// <summary>Stable setting key (see Common constants).</summary>
        public string SettingKey { get; set; }

        /// <summary>Setting value, stored as invariant text.</summary>
        public string SettingValue { get; set; }

        /// <summary>UTC timestamp when the setting was created.</summary>
        public DateTime CreatedAtUtc { get; set; }

        /// <summary>UTC timestamp of the last update. Null if never updated.</summary>
        public DateTime? UpdatedAtUtc { get; set; }
    }
}
