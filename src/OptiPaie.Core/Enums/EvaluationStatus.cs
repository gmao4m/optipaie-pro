namespace OptiPaie.Core.Enums
{
    /// <summary>Whether an employee's evaluation for a period has been completed.</summary>
    public enum EvaluationStatus
    {
        /// <summary>Not yet scored / still a draft.</summary>
        Pending = 1,

        /// <summary>Scored and finalised.</summary>
        Done = 2
    }
}
