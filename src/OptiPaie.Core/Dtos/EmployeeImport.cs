using System.Collections.Generic;

namespace OptiPaie.Core.Dtos
{
    /// <summary>File format for employee import/export.</summary>
    public enum ImportFileFormat
    {
        Xlsx = 1,
        Csv = 2
    }

    /// <summary>What to do when the file contains an employee that already exists (or repeats within the file).</summary>
    public enum DuplicateResolution
    {
        /// <summary>Import the new employees, skip the duplicates.</summary>
        SkipDuplicates = 1,

        /// <summary>Write nothing; let the user fix the file first.</summary>
        Stop = 2
    }

    /// <summary>Per-row outcome after validation.</summary>
    public enum ImportRowStatus
    {
        /// <summary>A new, valid employee that will be created.</summary>
        New = 1,

        /// <summary>Matches an existing employee (or an earlier row): handled per <see cref="DuplicateResolution"/>.</summary>
        Duplicate = 2,

        /// <summary>Has one or more validation problems: blocks the whole import until fixed.</summary>
        Error = 3
    }

    /// <summary>One importable/exportable employee column (field + bilingual header + the kind of value).</summary>
    public sealed class ImportColumn
    {
        public ImportColumn(string key, string headerFr, string headerAr, bool required, string example)
        {
            Key = key; HeaderFr = headerFr; HeaderAr = headerAr; Required = required; Example = example;
        }

        public string Key { get; }
        public string HeaderFr { get; }
        public string HeaderAr { get; }
        public bool Required { get; }
        public string Example { get; }

        /// <summary>The bilingual header written to the template/export, e.g. "Nom / اللقب".</summary>
        public string Header => HeaderFr + " / " + HeaderAr;
    }

    /// <summary>A raw, parsed table (a header row + data rows of strings) read from xlsx/csv.</summary>
    public sealed class ImportTable
    {
        public ImportTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
        {
            Headers = headers ?? new List<string>();
            Rows = rows ?? new List<IReadOnlyList<string>>();
        }

        public IReadOnlyList<string> Headers { get; }
        public IReadOnlyList<IReadOnlyList<string>> Rows { get; }
    }

    /// <summary>One problem found in a row, located to a column, with a bilingual message.</summary>
    public sealed class ImportIssue
    {
        public ImportIssue(int rowNumber, string column, string message)
        {
            RowNumber = rowNumber; Column = column; Message = message;
        }

        /// <summary>1-based row number as the user sees it in the file (header is row 1; first data row is 2).</summary>
        public int RowNumber { get; }
        public string Column { get; }
        public string Message { get; }
    }

    /// <summary>One previewed row: its status, a display name, and any issues.</summary>
    public sealed class ImportRowPreview
    {
        public ImportRowPreview(int rowNumber, string displayName, ImportRowStatus status, IReadOnlyList<ImportIssue> issues)
        {
            RowNumber = rowNumber; DisplayName = displayName; Status = status; Issues = issues ?? new List<ImportIssue>();
        }

        public int RowNumber { get; }
        public string DisplayName { get; }
        public ImportRowStatus Status { get; }
        public IReadOnlyList<ImportIssue> Issues { get; }
    }

    /// <summary>The full dry-run result shown before anything is written.</summary>
    public sealed class ImportPreview
    {
        public ImportPreview(
            IReadOnlyList<ImportRowPreview> rows,
            IReadOnlyList<ImportIssue> issues,
            IReadOnlyList<string> missingColumns)
        {
            Rows = rows ?? new List<ImportRowPreview>();
            Issues = issues ?? new List<ImportIssue>();
            MissingColumns = missingColumns ?? new List<string>();
        }

        public IReadOnlyList<ImportRowPreview> Rows { get; }

        /// <summary>Every per-row issue, flattened (row + column + message).</summary>
        public IReadOnlyList<ImportIssue> Issues { get; }

        /// <summary>Required columns that could not be found/mapped in the file.</summary>
        public IReadOnlyList<string> MissingColumns { get; }

        public int TotalRows => Rows.Count;
        public int NewCount => Count(ImportRowStatus.New);
        public int DuplicateCount => Count(ImportRowStatus.Duplicate);
        public int ErrorCount => Count(ImportRowStatus.Error);

        /// <summary>True only when the import can proceed: columns resolved and no row-level errors.</summary>
        public bool CanImport => MissingColumns.Count == 0 && ErrorCount == 0 && NewCount + DuplicateCount > 0;

        private int Count(ImportRowStatus s)
        {
            int n = 0;
            foreach (ImportRowPreview r in Rows) if (r.Status == s) n++;
            return n;
        }
    }

    /// <summary>What a committed import actually did.</summary>
    public sealed class ImportSummary
    {
        public ImportSummary(int created, int skippedDuplicates)
        {
            Created = created; SkippedDuplicates = skippedDuplicates;
        }

        public int Created { get; }
        public int SkippedDuplicates { get; }
    }
}
