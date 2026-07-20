using System;

namespace OptiPaie.Common.Validation
{
    /// <summary>
    /// Lightweight argument guards for fail-fast checks at method boundaries.
    /// These protect against programming errors (nulls, empty keys); user-facing
    /// validation is handled by <see cref="IValidator{T}"/> and <see cref="ValidationResult"/>.
    /// </summary>
    public static class Guard
    {
        /// <summary>Throws <see cref="ArgumentNullException"/> when <paramref name="value"/> is null.</summary>
        public static T AgainstNull<T>(T value, string paramName) where T : class
        {
            if (value == null)
            {
                throw new ArgumentNullException(paramName);
            }

            return value;
        }

        /// <summary>Throws <see cref="ArgumentException"/> when the string is null or whitespace.</summary>
        public static string AgainstNullOrWhiteSpace(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value cannot be null or empty.", paramName);
            }

            return value;
        }

        /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> when the value is negative.</summary>
        public static decimal AgainstNegative(decimal value, string paramName)
        {
            if (value < 0m)
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Value cannot be negative.");
            }

            return value;
        }
    }
}
