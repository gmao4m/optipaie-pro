using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OptiPaie.Common.Validation
{
    /// <summary>
    /// Accumulates validation findings for an operation. Acts as a small builder
    /// (findings are added as rules run) and then exposes an immutable view.
    /// <see cref="IsValid"/> is true when there are no <see cref="ValidationSeverity.Error"/>
    /// findings; warnings do not block.
    /// </summary>
    public sealed class ValidationResult
    {
        private readonly List<ValidationMessage> _messages = new List<ValidationMessage>();

        /// <summary>All findings collected so far.</summary>
        public IReadOnlyList<ValidationMessage> Messages => new ReadOnlyCollection<ValidationMessage>(_messages);

        /// <summary>True when there are no error-severity findings.</summary>
        public bool IsValid => !_messages.Any(m => m.Severity == ValidationSeverity.Error);

        /// <summary>True when at least one warning is present.</summary>
        public bool HasWarnings => _messages.Any(m => m.Severity == ValidationSeverity.Warning);

        /// <summary>The error-severity findings.</summary>
        public IEnumerable<ValidationMessage> Errors => _messages.Where(m => m.Severity == ValidationSeverity.Error);

        /// <summary>The warning-severity findings.</summary>
        public IEnumerable<ValidationMessage> Warnings => _messages.Where(m => m.Severity == ValidationSeverity.Warning);

        /// <summary>Adds an error finding.</summary>
        public void AddError(string code, string defaultMessage, string memberName = "")
        {
            _messages.Add(new ValidationMessage(ValidationSeverity.Error, code, defaultMessage, memberName));
        }

        /// <summary>Adds a warning finding.</summary>
        public void AddWarning(string code, string defaultMessage, string memberName = "")
        {
            _messages.Add(new ValidationMessage(ValidationSeverity.Warning, code, defaultMessage, memberName));
        }

        /// <summary>Adds an existing finding.</summary>
        public void Add(ValidationMessage message)
        {
            if (message != null)
            {
                _messages.Add(message);
            }
        }

        /// <summary>Merges the findings of another result into this one.</summary>
        public void Merge(ValidationResult other)
        {
            if (other != null)
            {
                _messages.AddRange(other._messages);
            }
        }
    }
}
