using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using OptiPaie.Common.Logging;

namespace OptiPaie.Services.Updates
{
    /// <summary>
    /// Launches the downloaded installer SAFELY, then closes the app.
    /// <para>
    /// The 1.29.0 incident: <c>Process.Start(setup); Environment.Exit(0)</c> started the installer
    /// while the app still held its files, corrupting the in-place upgrade. The correct sequence is:
    /// the app closes COMPLETELY → wait until its process is really gone and its files released →
    /// only THEN start the VISIBLE installer → and reopen the app afterwards, so a cancelled or
    /// failed install never leaves the user with a disappeared app.
    /// </para>
    /// <para>
    /// A tiny DETACHED helper (decoupled from this process so it survives the app's exit) performs
    /// that sequence. The UPDATE helper is a <b>cmd.exe batch</b>, NOT PowerShell: many client
    /// machines are locked down by Group Policy to <c>Restricted</c>/<c>AllSigned</c> PowerShell
    /// execution — which silently blocks a <c>-File script.ps1</c> even with <c>-ExecutionPolicy
    /// Bypass</c> — so "click update → nothing happens". cmd is always allowed. (The elevated MSI
    /// repair still uses PowerShell for its <c>-Verb RunAs</c> UAC prompt.)
    /// </para>
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

            int pid = Process.GetCurrentProcess().Id;
            RunDetachedHelper(BuildUpdateScript(pid, setupPath, CurrentExePath()), "apply_update.cmd", logger,
                "installer", useCmd: true);
        }

        /// <summary>
        /// Repairs the installed product (missing files) from the Windows Installer cache, using the
        /// SAME safe sequence but via PowerShell — the elevated <c>msiexec</c> repair needs a UAC
        /// prompt (<c>-Verb RunAs</c>). No download / no internet needed.
        /// </summary>
        public static void LaunchRepairAndExit(string productCode, ILogger logger)
        {
            if (string.IsNullOrEmpty(productCode))
            {
                throw new InvalidOperationException("No installed product to repair.");
            }

            int pid = Process.GetCurrentProcess().Id;
            RunDetachedHelper(BuildRepairScript(pid, productCode, CurrentExePath()), "repair.ps1", logger,
                "repair", useCmd: false);
        }

        private static void RunDetachedHelper(string script, string fileName, ILogger logger, string what, bool useCmd)
        {
            string dir = Path.Combine(Path.GetTempPath(), "OptiPaiePRO-Update");
            Directory.CreateDirectory(dir);
            string helper = Path.Combine(dir, fileName);
            // ANSI for the .cmd (cmd reads the OEM/ANSI codepage); the app path is ASCII in practice.
            File.WriteAllText(helper, script, useCmd ? System.Text.Encoding.Default : new System.Text.UTF8Encoding(false));

            ProcessStartInfo psi = useCmd
                ? new ProcessStartInfo("cmd.exe", "/c \"" + helper + "\"")
                : new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + helper + "\"");
            psi.UseShellExecute = true;                  // decoupled from this process (survives our exit)
            psi.WindowStyle = ProcessWindowStyle.Hidden; // the helper is invisible; the installer UI is not
            psi.CreateNoWindow = true;
            Process.Start(psi);

            if (logger != null)
            {
                logger.Info("Update: detached " + what + " helper started (" + (useCmd ? "cmd" : "powershell") +
                            "); app exiting so its files are released.");
            }

            // Give the helper a moment to reach its wait-loop, then exit FULLY so this app's files
            // are unlocked before the installer/repair touches them.
            System.Threading.Thread.Sleep(700);
            Environment.Exit(0);
        }

        /// <summary>The detached UPDATE batch (cmd) — exposed for test verification of the sequencing.
        /// (1) wait for the app PID to fully exit; (2) small settle delay; (3) run the VISIBLE
        /// installer and wait for it (its own UAC + progress show); (4) reopen the app — the new
        /// version on success, the intact old one on a cancelled/failed install.</summary>
        public static string BuildUpdateScript(int pid, string setup, string app)
        {
            return
                "@echo off\r\n" +
                ":wait\r\n" +
                "tasklist /FI \"PID eq " + pid + "\" /NH 2>nul | find \"" + pid + "\" >nul\r\n" +
                "if not errorlevel 1 ( ping -n 2 127.0.0.1 >nul & goto wait )\r\n" +
                "ping -n 3 127.0.0.1 >nul\r\n" +
                "start \"\" /wait \"" + Cmd(setup) + "\"\r\n" +
                "start \"\" \"" + Cmd(app) + "\"\r\n";
        }

        /// <summary>The detached REPAIR script (PowerShell — needs -Verb RunAs for elevation).</summary>
        public static string BuildRepairScript(int pid, string productCode, string app)
        {
            return
                "$ErrorActionPreference='SilentlyContinue'\r\n" +
                "while (Get-Process -Id " + pid + " -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }\r\n" +
                "Start-Sleep -Seconds 2\r\n" +
                "try { Start-Process msiexec -ArgumentList '/fomus " + productCode + " /qb' -Verb RunAs -Wait } catch {}\r\n" +
                "Start-Process -FilePath '" + Ps(app) + "'\r\n";
        }

        /// <summary>The running exe path — safe if <see cref="Process.MainModule"/> is inaccessible.</summary>
        private static string CurrentExePath()
        {
            try
            {
                ProcessModule m = Process.GetCurrentProcess().MainModule;
                if (m != null && !string.IsNullOrEmpty(m.FileName)) return m.FileName;
            }
            catch { /* MainModule can throw under some security contexts — fall back */ }

            try
            {
                string loc = Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrEmpty(loc)) return loc;
            }
            catch { }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OptiPaie PRO.exe");
        }

        /// <summary>Escapes a path for a cmd.exe batch (only '%' is special inside a quoted argument).</summary>
        private static string Cmd(string value) => (value ?? string.Empty).Replace("%", "%%");

        /// <summary>Escapes a path for a PowerShell single-quoted string literal.</summary>
        private static string Ps(string value) => (value ?? string.Empty).Replace("'", "''");
    }
}
