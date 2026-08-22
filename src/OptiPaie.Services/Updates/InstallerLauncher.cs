using System;
using System.Diagnostics;
using System.IO;
using OptiPaie.Common.Logging;

namespace OptiPaie.Services.Updates
{
    /// <summary>
    /// Launches the downloaded installer SAFELY, then closes the app.
    /// <para>
    /// The 1.29.0 incident: the old code did <c>Process.Start(setup); Environment.Exit(0)</c> — the
    /// installer started while the app was still tearing down and holding its files, and the
    /// in-place upgrade could corrupt (leaving a required DLL missing). The correct sequence is:
    /// the app closes COMPLETELY → wait until its process is really gone and its files released →
    /// only THEN start the VISIBLE installer → and reopen the app afterwards so a cancelled or
    /// failed install never leaves the user with a disappeared app.
    /// </para>
    /// A tiny DETACHED PowerShell helper (decoupled from this process so it survives the app's
    /// exit) performs that sequence. PowerShell's <c>Start-Process</c> reliably launches the
    /// visible installer and reopens the app even though the helper itself runs hidden.
    /// </summary>
    public static class InstallerLauncher
    {
        /// <summary>Runs the downloaded installer after the app fully exits, then reopens the app.</summary>
        public static void LaunchAndExit(string setupPath, ILogger logger)
        {
            if (string.IsNullOrEmpty(setupPath) || !File.Exists(setupPath))
            {
                throw new InvalidOperationException("No downloaded installer to launch.");
            }

            Process self = Process.GetCurrentProcess();
            RunDetachedHelper(BuildUpdateScript(self.Id, setupPath, self.MainModule.FileName), "apply_update.ps1", logger,
                "installer");
        }

        /// <summary>
        /// Repairs the installed product (missing files) from the Windows Installer cache, using
        /// the SAME safe sequence: wait for the app to exit, run an elevated + VISIBLE msiexec
        /// repair, then reopen the app. No download / no internet needed.
        /// </summary>
        public static void LaunchRepairAndExit(string productCode, ILogger logger)
        {
            if (string.IsNullOrEmpty(productCode))
            {
                throw new InvalidOperationException("No installed product to repair.");
            }

            Process self = Process.GetCurrentProcess();
            RunDetachedHelper(BuildRepairScript(self.Id, productCode, self.MainModule.FileName), "repair.ps1", logger,
                "repair");
        }

        private static void RunDetachedHelper(string script, string fileName, ILogger logger, string what)
        {
            string dir = Path.Combine(Path.GetTempPath(), "OptiPaiePRO-Update");
            Directory.CreateDirectory(dir);
            string helper = Path.Combine(dir, fileName);
            File.WriteAllText(helper, script);

            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + helper + "\"")
            {
                UseShellExecute = true,                  // decoupled from this process (survives our exit)
                WindowStyle = ProcessWindowStyle.Hidden  // the helper is invisible; the installer UI is not
            };
            Process.Start(psi);

            if (logger != null)
            {
                logger.Info("Update: detached " + what + " helper started; app exiting so its files are released.");
            }

            // Give the helper a moment to reach its wait-loop, then exit FULLY so this app's files
            // are unlocked before the installer/repair touches them.
            System.Threading.Thread.Sleep(700);
            Environment.Exit(0);
        }

        /// <summary>The detached update script (exposed for test verification of the sequencing).</summary>
        public static string BuildUpdateScript(int pid, string setup, string app)
        {
            // (1) wait for the app PID to fully exit; (2) let file handles release; (3) run the
            // VISIBLE installer and wait for it (its UAC prompt + progress are shown); (4) reopen
            // the app — on success the new version, on a cancelled/failed install the intact old
            // version — so the app never just vanishes.
            return
                "$ErrorActionPreference='SilentlyContinue'\r\n" +
                "while (Get-Process -Id " + pid + " -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }\r\n" +
                "Start-Sleep -Seconds 2\r\n" +
                "try { Start-Process -FilePath '" + Ps(setup) + "' -Wait } catch {}\r\n" +
                "Start-Process -FilePath '" + Ps(app) + "'\r\n";
        }

        /// <summary>The detached repair script (exposed for test verification of the sequencing).</summary>
        public static string BuildRepairScript(int pid, string productCode, string app)
        {
            return
                "$ErrorActionPreference='SilentlyContinue'\r\n" +
                "while (Get-Process -Id " + pid + " -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }\r\n" +
                "Start-Sleep -Seconds 2\r\n" +
                "try { Start-Process msiexec -ArgumentList '/fomus " + productCode + " /qb' -Verb RunAs -Wait } catch {}\r\n" +
                "Start-Process -FilePath '" + Ps(app) + "'\r\n";
        }

        /// <summary>Escapes a path for a PowerShell single-quoted string literal.</summary>
        private static string Ps(string value)
        {
            return (value ?? string.Empty).Replace("'", "''");
        }
    }
}
