using System;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// Base type for all entities with a numeric surrogate key.
    /// <para>
    /// <see cref="Id"/> maps to the SQLite INTEGER PRIMARY KEY (64-bit).
    /// <see cref="CreatedAtUtc"/> is the creation timestamp in UTC.
    /// </para>
    /// </summary>
    public abstract class EntityBase
    {
        /// <summary>Primary key. Zero for a not-yet-persisted entity.</summary>
        public long Id { get; set; }

        /// <summary>UTC timestamp when the row was created.</summary>
        public DateTime CreatedAtUtc { get; set; }
    }
}
