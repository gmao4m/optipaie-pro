namespace OptiPaie.PayrollEngine.Pipeline
{
    /// <summary>
    /// One isolated step of the calculation pipeline. Rules run in ascending
    /// <see cref="Order"/>; a new rule can be added without modifying existing ones.
    /// </summary>
    internal interface IPayrollRule
    {
        /// <summary>Execution order within the pipeline.</summary>
        int Order { get; }

        /// <summary>Stable rule name (used in the trace/log).</summary>
        string Name { get; }

        /// <summary>Applies the rule to the calculation context.</summary>
        void Apply(PayrollCalculationContext context);
    }
}
