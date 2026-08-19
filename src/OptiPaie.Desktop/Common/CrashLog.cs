using System;
using System.IO;
using System.Text;
using System.Windows;

namespace OptiPaie.Desktop.Common
{
    /// <summary>
    /// Last-resort crash diagnostics, independent of the DI logger so it works even if
    /// composition fails. Captures the three failure classes that each bypass a different
    /// guard (UI-thread, background-thread, unobserved Task) and writes immediate,
    /// disk-flushed breadcrumbs so that even a hard process kill (e.g. StackOverflow, which
    /// no handler can catch) leaves the last step that ran.
    /// <para>Files live in <c>%LOCALAPPDATA%\OptiPaie PRO\logs\</c>:
    /// <c>crash.log</c> (full exception dumps) and <c>breadcrumbs.log</c> (step trail).</para>
    /// </summary>
    internal static class CrashLog
    {
        private static readonly object Gate = new object();
        private static string _dir;

        public static string Directory
        {
            get
            {
                if (_dir == null)
                {
                    try
                    {
                        _dir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "OptiPaie PRO", "logs");
                        System.IO.Directory.CreateDirectory(_dir);
                    }
                    catch { _dir = Path.GetTempPath(); }
                }
                return _dir;
            }
        }

        /// <summary>Wires the process-wide handlers. Call as early as possible in startup.</summary>
        public static void Install(Application app)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Fatal("AppDomain.UnhandledException" + (e.IsTerminating ? " (terminating)" : ""), e.ExceptionObject as Exception);

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Fatal("TaskScheduler.UnobservedTaskException", e.Exception);
                e.SetObserved();
            };

            if (app != null)
            {
                app.DispatcherUnhandledException += (s, e) =>
                {
                    Fatal("Application.DispatcherUnhandledException", e.Exception);

                    // Show the user a clear message and KEEP the process alive — a UI-thread
                    // exception must never silently close the app (e.g. on the login path).
                    try
                    {
                        MessageBox.Show(
                            "حدث خطأ تقني ولم يُغلق البرنامج. تم تسجيل التفاصيل في:\n" + Directory +
                            "\n\nUne erreur technique est survenue ; l'application reste ouverte.\n" +
                            "Détails enregistrés dans le dossier ci-dessus.",
                            "OptiPaie PRO", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch { /* showing the dialog must never itself crash the handler */ }

                    e.Handled = true;
                };
            }

            Breadcrumb("crash handlers installed · logs in " + Directory);
        }

        /// <summary>One step of a risky path, flushed to disk immediately.</summary>
        public static void Breadcrumb(string step)
        {
            Write("breadcrumbs.log", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + step);
        }

        /// <summary>Full exception dump (type · message · stack, all inner exceptions).</summary>
        public static void Fatal(string source, Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("========== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  [" + source + "] ==========");
            if (ex == null)
            {
                sb.AppendLine("(no exception object)");
            }
            else
            {
                int depth = 0;
                for (Exception e = ex; e != null; e = e.InnerException, depth++)
                {
                    sb.AppendLine(new string(' ', depth * 2) + e.GetType().FullName + ": " + e.Message);
                    sb.AppendLine(e.StackTrace);
                    sb.AppendLine("  ---");
                }
            }
            Write("crash.log", sb.ToString());
            Breadcrumb("FATAL logged from " + source);
        }

        private static void Write(string file, string text)
        {
            try
            {
                lock (Gate)
                {
                    using (var fs = new FileStream(Path.Combine(Directory, file), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var w = new StreamWriter(fs))
                    {
                        w.WriteLine(text);
                        w.Flush();
                        fs.Flush(true); // push OS buffers to disk now, so a hard kill can't lose it
                    }
                }
            }
            catch { /* diagnostics must never throw */ }
        }
    }
}
