using System;

namespace OptiPaie.Common.Logging
{
    /// <summary>
    /// A no-op logger, useful as a safe default and in unit tests.
    /// </summary>
    public sealed class NullLogger : ILogger
    {
        /// <summary>Shared singleton instance.</summary>
        public static readonly NullLogger Instance = new NullLogger();

        private NullLogger()
        {
        }

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message)
        {
        }

        public void Error(string message, Exception exception)
        {
        }
    }
}
