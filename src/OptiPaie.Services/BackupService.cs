using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using OptiPaie.Common.Configuration;
using OptiPaie.Common.Constants;
using OptiPaie.Common.Logging;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Orchestrates database backup and restore: delegates the SQLite specifics to
    /// <see cref="IDatabaseBackupProvider"/>, then records each backup with a
    /// checksum and prunes old files. No payroll logic.
    /// </summary>
    public sealed class BackupService : IBackupService
    {
        private const int KeepCount = 30;

        private readonly IDatabaseBackupProvider _provider;
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly AppConfiguration _configuration;
        private readonly ILogger _logger;

        public BackupService(
            IDatabaseBackupProvider provider,
            IUnitOfWorkFactory unitOfWorkFactory,
            AppConfiguration configuration,
            ILogger logger)
        {
            _provider = Guard.AgainstNull(provider, nameof(provider));
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _configuration = Guard.AgainstNull(configuration, nameof(configuration));
            _logger = Guard.AgainstNull(logger, nameof(logger));
        }

        public Result<BackupRecord> Backup(BackupType type)
        {
            try
            {
                Directory.CreateDirectory(_configuration.BackupDirectory);

                string fileName = "optipaie_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".db";
                string destinationPath = Path.Combine(_configuration.BackupDirectory, fileName);

                _provider.Backup(destinationPath);

                var record = new BackupRecord
                {
                    FilePath = destinationPath,
                    BackupType = type,
                    SizeBytes = new FileInfo(destinationPath).Length,
                    Checksum = ComputeChecksum(destinationPath),
                    SchemaVersion = _provider.GetSchemaVersion()
                };

                using (IUnitOfWork uow = _unitOfWorkFactory.Create())
                {
                    uow.BackupRecords.Insert(record);
                }

                PruneOldBackups();
                _logger.Info("Backup created: " + destinationPath);
                return Result.Ok(record);
            }
            catch (Exception ex)
            {
                _logger.Error("Backup failed.", ex);
                return Result.Fail<BackupRecord>("La sauvegarde de la base de données a échoué.", ErrorCodes.BackupFailed);
            }
        }

        public Result Restore(string backupFilePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
                {
                    return Result.Fail("Fichier de sauvegarde introuvable.", ErrorCodes.BackupFileNotFound);
                }

                if (!_provider.VerifyDatabaseFile(backupFilePath))
                {
                    return Result.Fail("Le fichier de sauvegarde est invalide ou corrompu.", ErrorCodes.BackupInvalidFile);
                }

                _provider.RestoreFrom(backupFilePath);
                _logger.Info("Database restored from: " + backupFilePath);
                return Result.Ok();
            }
            catch (Exception ex)
            {
                _logger.Error("Restore failed.", ex);
                return Result.Fail("La restauration de la base de données a échoué.", ErrorCodes.RestoreFailed);
            }
        }

        public IReadOnlyList<BackupRecord> GetRecent(int count)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.BackupRecords.GetRecent(count).ToList();
            }
        }

        private void PruneOldBackups()
        {
            try
            {
                List<FileInfo> files = Directory
                    .GetFiles(_configuration.BackupDirectory, "optipaie_*.db")
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.CreationTimeUtc)
                    .ToList();

                for (int i = KeepCount; i < files.Count; i++)
                {
                    try
                    {
                        files[i].Delete();
                    }
                    catch
                    {
                        // A locked/old file that cannot be pruned is not fatal.
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("Backup pruning failed: " + ex.Message);
            }
        }

        private static string ComputeChecksum(string filePath)
        {
            using (var sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(filePath))
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }
    }
}
