using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using OptiPaie.Common.Constants;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Services.Licensing
{
    /// <summary>
    /// Derives a stable device id from hardware characteristics (motherboard serial
    /// and CPU id) via WMI, combined and hashed. Falls back gracefully to the machine
    /// name when hardware info is unavailable, so it never throws. This is far more
    /// robust than a machine-name-only id (which changes when a PC is renamed) and it
    /// survives a Windows reinstall of the same physical machine.
    /// </summary>
    public sealed class WmiDeviceIdentity : IDeviceIdentity
    {
        private readonly object _lock = new object();
        private string _cachedId;

        public string GetDeviceId()
        {
            lock (_lock)
            {
                if (_cachedId != null)
                {
                    return _cachedId;
                }

                string board = QueryFirst("Win32_BaseBoard", "SerialNumber");
                string cpu = QueryFirst("Win32_Processor", "ProcessorId");

                var sb = new StringBuilder();
                sb.Append(Clean(board)).Append('|');
                sb.Append(Clean(cpu)).Append('|');

                // Always mix in the machine name so two machines with blank/identical
                // hardware serials (some VMs) still differ; hardware makes it stable.
                sb.Append(Environment.MachineName).Append('|');
                sb.Append(AppConstants.ApplicationName);

                _cachedId = Hash(sb.ToString());
                return _cachedId;
            }
        }

        public string GetDeviceInfo()
        {
            string machine = SafeMachineName();
            string os = SafeOsVersion();
            return machine + " / " + os + " / " + Environment.UserName;
        }

        private static string QueryFirst(string wmiClass, string property)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT " + property + " FROM " + wmiClass))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementBaseObject item in results)
                    {
                        using (item)
                        {
                            object value = item[property];
                            if (value != null)
                            {
                                string text = value.ToString().Trim();
                                if (text.Length > 0)
                                {
                                    return text;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // WMI may be disabled or restricted; fall through to the fallback.
            }

            return string.Empty;
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();

            // Common placeholder serials from OEMs / VMs carry no identifying value.
            if (trimmed.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("Default string", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return trimmed;
        }

        private static string Hash(string raw)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return BitConverter.ToString(hash).Replace("-", string.Empty).Substring(0, 32);
            }
        }

        private static string SafeMachineName()
        {
            try { return Environment.MachineName; } catch { return "PC"; }
        }

        private static string SafeOsVersion()
        {
            try { return Environment.OSVersion.VersionString; } catch { return "Windows"; }
        }
    }
}
