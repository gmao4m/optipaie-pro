using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services.Validation
{
    /// <summary>
    /// Bridges the validation pipeline to the service result type: turns a failed
    /// <see cref="ValidationResult"/> into a <see cref="Result"/> carrying the first
    /// error's code and message.
    /// </summary>
    internal static class ValidationResultExtensions
    {
        /// <summary>Returns a failed <see cref="Result"/> from the first error.</summary>
        public static Result ToFailure(this ValidationResult validation)
        {
            ValidationMessage error = validation.Errors.First();
            return Result.Fail(error.DefaultMessage, error.Code);
        }

        /// <summary>Returns a failed <see cref="Result{T}"/> from the first error.</summary>
        public static Result<T> ToFailure<T>(this ValidationResult validation)
        {
            ValidationMessage error = validation.Errors.First();
            return Result.Fail<T>(error.DefaultMessage, error.Code);
        }
    }
}
