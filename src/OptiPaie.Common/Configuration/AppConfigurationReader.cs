using System;
using System.Configuration;
using System.IO;
using OptiPaie.Common.Constants;

namespace OptiPaie.Common.Configuration
{
    /// <summary>
    /// Reads the machine-level bootstrap configuration from the host application's
    /// config file (app settings), applying sensible defaults: the data directory
    /// defaults to <c>%AppData%\OptiPaie DZ</c> and the language to French.
    /// </summary>
    public static class AppConfigurationReader
    {
        /// <summary>Reads and returns the bootstrap configuration.</summary>
        public static AppConfiguration Read()
        {
            string dataDirectory = ConfigurationManager.AppSettings["DataDirectory"];

            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                dataDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    AppConstants.DataFolderName);
            }

            string language = ConfigurationManager.AppSettings["DefaultLanguage"];

            if (string.IsNullOrWhiteSpace(language))
            {
                language = AppConstants.DefaultLanguage;
            }

            return new AppConfiguration(dataDirectory, language);
        }
    }
}
