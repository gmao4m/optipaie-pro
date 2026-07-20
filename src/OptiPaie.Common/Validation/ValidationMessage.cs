using System;

namespace OptiPaie.Common.Validation
{
    /// <summary>
    /// A single immutable validation finding. <see cref="Code"/> is a stable key the
    /// presentation layer resolves to a localised (Arabic/French) message;
    /// <see cref="DefaultMessage"/> is a developer-readable fallback.
    /// </summary>
    public sealed class ValidationMessage
    {
        /// <summary>Severity of the finding.</summary>
        public ValidationSeverity Severity { get; }

        /// <summary>Stable localisation key for the message.</summary>
        public string Code { get; }

        /// <summary>Developer-readable fallback message.</summary>
        public string DefaultMessage { get; }

        /// <summary>Name of the offending member/field, when applicable. May be empty.</summary>
        public string MemberName { get; }

        /// <summary>Creates a validation message.</summary>
        public ValidationMessage(ValidationSeverity severity, string code, string defaultMessage, string memberName = "")
        {
            if (string.IsNullOrEmpty(code))
            {
                throw new ArgumentException("A validation code is required.", nameof(code));
            }

            Severity = severity;
            Code = code;
            DefaultMessage = defaultMessage ?? string.Empty;
            MemberName = memberName ?? string.Empty;
        }
    }
}
