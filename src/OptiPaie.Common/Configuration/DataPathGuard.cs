using System;
using System.IO;

namespace OptiPaie.Common.Configuration
{
    /// <summary>
    /// Thrown when the configured data directory points at a network location. The message is
    /// bilingual (Arabic + French) and non-technical: it is shown to the customer as-is and the
    /// application refuses to start, because an SQLite database opened from two machines over a
    /// network share is silently corrupted. A dedicated type so startup can show THIS message
    /// (the shared-folder warning) rather than the generic fatal-error message.
    /// </summary>
    public sealed class DataPathException : Exception
    {
        public DataPathException(string message) : base(message) { }
    }

    /// <summary>
    /// Guards the data directory against network locations (UNC paths and mapped network drives).
    /// <para>
    /// What the customer gets: their payroll data is never silently corrupted by running the single
    /// -user SQLite database from a shared folder opened on two PCs. The app refuses to start on a
    /// network path and explains why, in plain Arabic and French, instead of losing data later.
    /// </para>
    /// Read-only, side-effect free and fully unit-testable (pure string/drive inspection).
    /// </summary>
    public static class DataPathGuard
    {
        /// <summary>
        /// The bilingual (AR + FR), non-technical refusal message shown to the customer.
        /// </summary>
        public const string NetworkPathMessage =
            "قاعدة بيانات OptiPaie يجب أن تكون على هذا الجهاز، لا على مجلّد مشترك أو قرص شبكة.\n" +
            "فتح نفس القاعدة من جهازين عبر الشبكة يُتلف بياناتك نهائيًّا.\n" +
            "صحّح موقع البيانات (اجعله على هذا الجهاز) ثم أعد تشغيل البرنامج.\n" +
            "\n" +
            "La base de données OptiPaie doit se trouver sur CET ordinateur, pas sur un dossier " +
            "partagé ni un lecteur réseau.\n" +
            "Ouvrir la même base depuis deux postes via le réseau corrompt définitivement vos données.\n" +
            "Corrigez l'emplacement des données (sur cet ordinateur) puis relancez le programme.";

        /// <summary>
        /// True when <paramref name="path"/> resolves to a UNC path (<c>\\server\share</c>) or a
        /// mapped network drive. Unparseable paths and drives whose type cannot be read are treated
        /// as NOT network (fail-open): the guard only blocks a location it can positively confirm is
        /// on the network, never a path it merely failed to inspect.
        /// </summary>
        public static bool IsNetworkPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                return false; // cannot parse → do not block on this check
            }

            // UNC path (\\server\share or //server/share).
            if (full.StartsWith("\\\\", StringComparison.Ordinal) ||
                full.StartsWith("//", StringComparison.Ordinal))
            {
                return true;
            }

            // Mapped network drive (e.g. Z:\ pointing at a share).
            try
            {
                string root = Path.GetPathRoot(full);
                if (!string.IsNullOrEmpty(root) && root.Length >= 2 && root[1] == ':')
                {
                    var drive = new DriveInfo(root);
                    if (drive.DriveType == DriveType.Network)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false; // drive not ready / not inspectable → do not block
            }

            return false;
        }

        /// <summary>
        /// Throws <see cref="DataPathException"/> with the bilingual message when the data directory
        /// is a network location; otherwise returns silently. Called at startup before the database
        /// is opened.
        /// </summary>
        public static void Validate(string dataDirectory)
        {
            if (IsNetworkPath(dataDirectory))
            {
                throw new DataPathException(NetworkPathMessage);
            }
        }
    }
}
