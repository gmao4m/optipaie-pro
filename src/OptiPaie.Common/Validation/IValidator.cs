namespace OptiPaie.Common.Validation
{
    /// <summary>
    /// Validates an instance and returns a <see cref="ValidationResult"/>. Validators
    /// hold the business validation rules for one entity type, keeping that logic
    /// isolated from services and repositories (SOLID: single responsibility).
    /// </summary>
    /// <typeparam name="T">The type being validated.</typeparam>
    public interface IValidator<in T>
    {
        /// <summary>Runs all rules and returns the accumulated findings.</summary>
        ValidationResult Validate(T instance);
    }
}
