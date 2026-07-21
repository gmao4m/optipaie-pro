namespace OptiPaie.Core.Enums
{
    /// <summary>Lifecycle of a training session.</summary>
    public enum TrainingStatus
    {
        /// <summary>Planned — not started yet.</summary>
        Planned = 1,

        /// <summary>In progress.</summary>
        Ongoing = 2,

        /// <summary>Finished.</summary>
        Completed = 3,

        /// <summary>Cancelled.</summary>
        Cancelled = 4
    }

    /// <summary>Outcome of one participant in a training session.</summary>
    public enum TrainingResult
    {
        /// <summary>Enrolled — outcome not yet known.</summary>
        Enrolled = 1,

        /// <summary>Completed successfully.</summary>
        Completed = 2,

        /// <summary>Did not pass.</summary>
        Failed = 3,

        /// <summary>Did not attend.</summary>
        Absent = 4
    }
}
