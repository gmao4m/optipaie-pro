using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// An immutable engine message (validation error, warning or note). Lives in
    /// Core so the engine has no dependency on the Common validation types.
    /// <see cref="Code"/> is a stable key the UI resolves to a localized message.
    /// </summary>
    public sealed class PayrollMessage
    {
        /// <summary>Severity of the message.</summary>
        public PayrollMessageSeverity Severity { get; }

        /// <summary>Stable localisation key.</summary>
        public string Code { get; }

        /// <summary>Developer-readable fallback text.</summary>
        public string Text { get; }

        /// <summary>Creates a payroll message.</summary>
        public PayrollMessage(PayrollMessageSeverity severity, string code, string text)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Text = text ?? string.Empty;
        }

        /// <summary>Creates an error message.</summary>
        public static PayrollMessage Error(string code, string text)
        {
            return new PayrollMessage(PayrollMessageSeverity.Error, code, text);
        }

        /// <summary>Creates a warning message.</summary>
        public static PayrollMessage Warning(string code, string text)
        {
            return new PayrollMessage(PayrollMessageSeverity.Warning, code, text);
        }

        /// <summary>Creates an informational message.</summary>
        public static PayrollMessage Info(string code, string text)
        {
            return new PayrollMessage(PayrollMessageSeverity.Info, code, text);
        }
    }
}
