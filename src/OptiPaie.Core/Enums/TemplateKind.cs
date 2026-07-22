namespace OptiPaie.Core.Enums
{
    /// <summary>
    /// The category of an evaluation template. The seven built-in kinds ship ready to
    /// use; <see cref="Custom"/> is any company-created template.
    /// </summary>
    public enum TemplateKind
    {
        General = 1,
        Sales = 2,
        Production = 3,
        Administrative = 4,
        Technical = 5,
        Management = 6,
        Probation = 7,
        Custom = 8
    }
}
