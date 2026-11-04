using System.Collections.Generic;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Core.Interfaces.Services
{
    /// <summary>
    /// Import and export of employees for the ACTIVE company. Export and the template produce an
    /// xlsx (or csv) whose headers match exactly what the import expects, so an export can be edited
    /// and re-imported without loss. Import is a strict, atomic, validate-before-write pipeline:
    /// read → preview → confirm. Nothing is written until <see cref="Commit"/>, which runs in a
    /// single transaction (all rows or none) and never touches another company's data.
    /// </summary>
    public interface IEmployeeImportExportService
    {
        /// <summary>The importable/exportable columns, in order — for the header row and the mapping UI.</summary>
        IReadOnlyList<ImportColumn> Columns { get; }

        /// <summary>Writes an empty template (header row + one example row) to <paramref name="path"/>.</summary>
        void WriteTemplate(string path, ImportFileFormat format);

        /// <summary>Exports every employee of the ACTIVE company to <paramref name="path"/>.</summary>
        void Export(long companyId, string path, ImportFileFormat format);

        /// <summary>Reads an xlsx/csv file into a raw header+rows table (format detected by extension).</summary>
        ImportTable Read(string path);

        /// <summary>
        /// Validates the whole table against the active company WITHOUT writing anything, returning a
        /// preview: how many rows are new / duplicate / in error, and each problem located to its row
        /// and column. <paramref name="mapping"/> maps a column Key to the source header in the file;
        /// pass null/empty to auto-match by header (the template and an export match automatically).
        /// </summary>
        ImportPreview BuildPreview(long companyId, ImportTable table, IReadOnlyDictionary<string, string> mapping);

        /// <summary>
        /// Re-validates the SAME table/mapping shown in the preview and writes the import in ONE
        /// transaction: creates every new employee (and its draft contract, exactly as manual entry
        /// does), applying <paramref name="resolution"/> to duplicates. Refuses — writing nothing — if
        /// any row has a validation error, a required column is unmapped, or duplicates exist and the
        /// resolution is <see cref="DuplicateResolution.Stop"/>. Records one audit entry. Re-parsing
        /// (rather than trusting a display object) guarantees what was previewed is exactly what is written.
        /// </summary>
        Result<ImportSummary> Commit(long companyId, ImportTable table, IReadOnlyDictionary<string, string> mapping,
            DuplicateResolution resolution, string fileName);
    }
}
