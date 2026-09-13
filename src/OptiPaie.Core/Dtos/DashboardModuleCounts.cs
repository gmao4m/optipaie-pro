namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// Active-loan portfolio of one company, computed by a single SQL aggregate
    /// (no per-loan repayment query). <see cref="TotalOutstanding"/> already clamps each
    /// loan's balance at zero, exactly like the per-loan summary.
    /// </summary>
    public sealed class LoanPortfolio
    {
        public int ActiveCount { get; set; }
        public decimal TotalOutstanding { get; set; }
    }

    /// <summary>
    /// Recruitment counters of one company, computed by SQL aggregates (no per-posting
    /// candidate query): open postings and the total number of candidates across every posting.
    /// </summary>
    public sealed class RecruitmentCounts
    {
        public int OpenPostings { get; set; }
        public int Candidates { get; set; }
    }
}
