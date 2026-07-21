using System;

namespace OptiPaie.Core.Licensing
{
    /// <summary>Immutable snapshot of the local trial state.</summary>
    public sealed class TrialInfo
    {
        public TrialInfo(bool hasStarted, DateTime? startedUtc, DateTime? expiresUtc, DateTime asOfUtc)
        {
            HasStarted = hasStarted;
            StartedUtc = startedUtc;
            ExpiresUtc = expiresUtc;

            if (hasStarted && expiresUtc.HasValue)
            {
                double totalHours = (expiresUtc.Value - asOfUtc).TotalHours;
                HoursRemaining = totalHours <= 0 ? 0 : (int)Math.Ceiling(totalHours);
                DaysRemaining = HoursRemaining <= 0 ? 0 : (int)Math.Ceiling(HoursRemaining / 24.0);
                IsActive = asOfUtc < expiresUtc.Value;
            }
            else
            {
                HoursRemaining = 0;
                DaysRemaining = 0;
                IsActive = false;
            }
        }

        /// <summary>True once the customer has started the trial at least once.</summary>
        public bool HasStarted { get; }

        public DateTime? StartedUtc { get; }
        public DateTime? ExpiresUtc { get; }

        /// <summary>Whole days left (0 when expired).</summary>
        public int DaysRemaining { get; }

        /// <summary>Whole hours left (0 when expired). This is the primary countdown for the 48 h trial.</summary>
        public int HoursRemaining { get; }

        /// <summary>Human-friendly remaining time, e.g. "1 j 6 h" or "9 h".</summary>
        public string RemainingText
        {
            get
            {
                if (HoursRemaining <= 0) return "0 h";
                int days = HoursRemaining / 24;
                int hours = HoursRemaining % 24;
                if (days > 0 && hours > 0) return days + " j " + hours + " h";
                if (days > 0) return days + " j";
                return hours + " h";
            }
        }

        /// <summary>True while the trial is still valid.</summary>
        public bool IsActive { get; }

        /// <summary>True when the trial was started and has now elapsed.</summary>
        public bool IsExpired => HasStarted && !IsActive;

        public static TrialInfo NotStarted()
        {
            return new TrialInfo(false, null, null, DateTime.UtcNow);
        }
    }

    /// <summary>Manages the local, offline 30-day trial (no server involved).</summary>
    public interface ITrialService
    {
        /// <summary>Current trial status (anti clock-rollback aware).</summary>
        TrialInfo GetStatus();

        /// <summary>Starts the trial if it has never been started; returns the resulting status.</summary>
        TrialInfo StartTrial();
    }
}
