using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper-based persistence for <see cref="ArchiveDocument"/>.</summary>
    internal sealed class ArchiveDocumentRepository : RepositoryBase, IArchiveDocumentRepository
    {
        public ArchiveDocumentRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public ArchiveDocument GetById(long id)
        {
            return Connection.QuerySingleOrDefault<ArchiveDocument>(
                "SELECT * FROM ArchiveDocuments WHERE Id = @id;",
                new { id }, Transaction);
        }

        public ArchiveDocument GetByPayslipAndLanguage(long payslipId, string languageCode)
        {
            return Connection.QuerySingleOrDefault<ArchiveDocument>(
                "SELECT * FROM ArchiveDocuments " +
                "WHERE PayslipId = @payslipId AND LanguageCode = @languageCode " +
                "ORDER BY CreatedAtUtc DESC, Id DESC LIMIT 1;",
                new { payslipId, languageCode }, Transaction);
        }

        public IEnumerable<ArchiveDocument> GetByPayslip(long payslipId)
        {
            return Connection.Query<ArchiveDocument>(
                "SELECT * FROM ArchiveDocuments WHERE PayslipId = @payslipId ORDER BY CreatedAtUtc DESC, Id DESC;",
                new { payslipId }, Transaction);
        }

        public long Insert(ArchiveDocument document)
        {
            document.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO ArchiveDocuments " +
                "(PayslipId, LanguageCode, PdfContent, SnapshotJson, Checksum, CreatedAtUtc) " +
                "VALUES " +
                "(@PayslipId, @LanguageCode, @PdfContent, @SnapshotJson, @Checksum, @CreatedAtUtc); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, document, Transaction);
            document.Id = id;
            return id;
        }
    }
}
