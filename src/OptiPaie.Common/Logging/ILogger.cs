using System;

namespace OptiPaie.Common.Logging
{
    /// <summary>
    /// Minimal logging abstraction. Implementations write diagnostics; user-facing
    /// errors never reach the log path — those go through localized messages.
    /// </summary>
    public interface ILogger
    {
        /// <summary>Logs an informational message.</summary>
        void Info(string message);

        /// <summary>Logs a warning.</summary>
        void Warn(string message);

        /// <summary>Logs an error message.</summary>
        void Error(string message);

        /// <summary>Logs an error message with the associated exception.</summary>
        void Error(string message, Exception exception);
    }
}
