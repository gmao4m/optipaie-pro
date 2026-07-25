namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// How an evaluation criterion is scored.
    /// <see cref="Behavioral"/> is rated 1-5 directly by the evaluator;
    /// <see cref="Kpi"/> is numeric — the evaluator enters a target and an achieved value
    /// and the system converts the ratio into a 1-5 score.
    /// </summary>
    public enum CriterionType
    {
        Behavioral = 1,
        Kpi = 2
    }
}
