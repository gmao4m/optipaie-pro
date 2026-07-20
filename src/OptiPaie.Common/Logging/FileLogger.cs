using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace OptiPaie.Common.Logging
{
    /// <summary>
    /// Thread-safe file logger that appends timestamped entries to a single log
    /// file. Logging never throws to the caller: if writing fails (e.g. the file is
    /// locked) the entry is silently dropped, because diagnostics must never break
    /// the application.
    /// </summary>
    public sealed class FileLogger : ILogger
    {
        private readonly string _logFilePath;
        private readonly object _syncRoot = new object();

        /// <summary>Creates a logger writing to the given file path.</summary>
        public FileLogger(string logFilePath)
        {
            if (string.IsNullOrWhiteSpace(logFilePath))
            {
                throw new ArgumentException("Log file path must be provided.", nameof(logFilePath));
            }

            _logFilePath = logFilePath;
        }

        public void Info(string message)
        {
            Write("INFO", message, null);
        }

        public void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public void Error(string message)
        {
            Write("ERROR", message, null);
        }

        public void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        private void Write(string level, string message, Exception exception)
        {
            try
            {
                var entry = new StringBuilder();
                entry.Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                entry.Append(" [").Append(level).Append("] ");
                entry.Append(message);

                if (exception != null)
                {
                    entry.AppendLine();
                    entry.Append(exception);
                }

                lock (_syncRoot)
                {
                    string directory = Path.GetDirectoryName(_logFilePath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.AppendAllText(_logFilePath, entry.AppendLine().ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never throw to the caller.
            }
        }
    }
}
