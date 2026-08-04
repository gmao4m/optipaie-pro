namespace OptiPaie.Core.Enums
{
    /// <summary>The nature of an evaluation criterion.</summary>
    public enum CriterionCategory
    {
        /// <summary>Behaviour / soft skills (attitude, teamwork, punctuality…).</summary>
        Behavioral = 1,

        /// <summary>Technical / job-craft competence.</summary>
        Technical = 2,

        /// <summary>Administrative rigor (accuracy, process, deadlines…).</summary>
        Administrative = 3,

        /// <summary>A numeric objective scored from target vs achieved.</summary>
        Kpi = 4
    }
}
