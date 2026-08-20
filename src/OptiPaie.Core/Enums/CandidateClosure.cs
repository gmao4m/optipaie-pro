namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// How a candidate's file was closed negatively. Stored additively alongside
    /// <see cref="CandidateStage.Rejected"/> (the CHECK constraint stays 1..6), so a closed
    /// file is told apart between a refusal by us and a withdrawal by the candidate — each
    /// with a mandatory reason.
    /// </summary>
    public enum CandidateClosure
    {
        /// <summary>Refusé — closed by the company.</summary>
        Rejected = 1,

        /// <summary>Désisté — the candidate withdrew.</summary>
        Withdrawn = 2
    }
}
