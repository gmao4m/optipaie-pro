using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence operations for <see cref="ArchiveDocument"/>.</summary>
    public interface IArchiveDocumentRepository
    {
        /// <summary>Returns the document with the given id, or null.</summary>
        ArchiveDocument GetById(long id);

        /// <summary>Returns the archived document for a payslip in a given language, or null.</summary>
        ArchiveDocument GetByPayslipAndLanguage(long payslipId, string languageCode);

        /// <summary>Returns all archived documents for a payslip.</summary>
        IEnumerable<ArchiveDocument> GetByPayslip(long payslipId);

        /// <summary>Inserts an archived document and returns its new id.</summary>
        long Insert(ArchiveDocument document);
    }
}
