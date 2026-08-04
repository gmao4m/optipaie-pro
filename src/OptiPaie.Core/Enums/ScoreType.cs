namespace OptiPaie.Core.Enums
{
    /// <summary>How a (non-KPI) criterion is scored by the evaluator. Whatever the type,
    /// the value is normalised to 0-100 for a fair, comparable total.</summary>
    public enum ScoreType
    {
        /// <summary>1 to 5 stars.</summary>
        Stars5 = 1,

        /// <summary>0 to 20.</summary>
        Score20 = 2,

        /// <summary>0 to 100 percent.</summary>
        Percent = 3
    }
}
