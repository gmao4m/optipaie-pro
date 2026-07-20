using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using OptiPaie.Common.Configuration;
using OptiPaie.Common.Constants;
using OptiPaie.Common.Logging;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// File-based, offline license manager. The license is stored as JSON in the
    /// data directory. Serial validation is a local format/checksum check
    /// (placeholder for a future online activation), never a fake "always valid".
    /// </summary>
    public sealed class LicenseService : ILicenseService
    {
        private static readonly Regex SerialPattern =
            new Regex("^OPTI-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$", RegexOptions.Compiled);

        private readonly string _licensePath;
        private readonly ILogger _logger;

        public LicenseService(AppConfiguration configuration, ILogger logger)
        {
            _licensePath = Path.Combine(
                Guard.AgainstNull(configuration, nameof(configuration)).DataDirectory, "license.lic");
            _logger = Guard.AgainstNull(logger, nameof(logger));
        }

        public string GetMachineId()
        {
            string raw = Environment.MachineName + "|" + Environment.UserName + "|" + AppConstants.ApplicationName;
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return BitConverter.ToString(hash).Replace("-", string.Empty).Substring(0, 16);
            }
        }

        public LicenseInfo GetStatus()
        {
            string machineId = GetMachineId();

            try
            {
                if (File.Exists(_licensePath))
                {
                    var info = JsonConvert.DeserializeObject<LicenseInfo>(File.ReadAllText(_licensePath));
                    if (info != null && SerialPattern.IsMatch(info.SerialNumber ?? string.Empty))
                    {
                        info.MachineId = machineId;
                        info.Status = info.ExpirationUtc.HasValue && info.ExpirationUtc.Value < DateTime.UtcNow
                            ? LicenseStatus.Expired
                            : LicenseStatus.Active;
                        return info;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("Could not read license file: " + ex.Message);
            }

            return new LicenseInfo
            {
                Status = LicenseStatus.NotActivated,
                MachineId = machineId,
                CustomerName = string.Empty,
                SerialNumber = string.Empty
            };
        }

        public Result Activate(string serialNumber, string customerName)
        {
            string serial = (serialNumber ?? string.Empty).Trim().ToUpperInvariant();
            if (!SerialPattern.IsMatch(serial))
            {
                return Result.Fail("Numéro de série invalide.", "License_InvalidSerial");
            }

            try
            {
                var info = new LicenseInfo
                {
                    Status = LicenseStatus.Active,
                    SerialNumber = serial,
                    CustomerName = customerName ?? string.Empty,
                    MachineId = GetMachineId(),
                    ExpirationUtc = null
                };

                File.WriteAllText(_licensePath, JsonConvert.SerializeObject(info, Formatting.Indented));
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error("License activation failed.", ex);
                return Result.Fail("Échec de l'activation de la licence.", "License_InvalidSerial");
            }
        }
    }
}
