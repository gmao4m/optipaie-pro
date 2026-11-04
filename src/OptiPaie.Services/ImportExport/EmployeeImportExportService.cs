using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using OptiPaie.Common.Constants;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services.ImportExport
{
    /// <summary>
    /// Imports/exports employees for the ACTIVE company. See <see cref="IEmployeeImportExportService"/>.
    /// The validation/duplicate/commit pipeline is format-agnostic (works on an <see cref="ImportTable"/>),
    /// so it is fully unit-tested via CSV with no Excel dependency; xlsx is a thin ClosedXML adapter.
    /// </summary>
    public sealed class EmployeeImportExportService : IEmployeeImportExportService
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly IValidator<Employee> _validator;

        public EmployeeImportExportService(IUnitOfWorkFactory unitOfWorkFactory, IValidator<Employee> validator)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _validator = Guard.AgainstNull(validator, nameof(validator));
        }

        /// <summary>Audit sink (records who/when/how-many/which-file). Best-effort; wired by composition.</summary>
        public IAuditSink Audit { get; set; } = NullAuditSink.Instance;

        public IReadOnlyList<ImportColumn> Columns => EmployeeImportColumns.All;

        // ------------------------------------------------------------------ template / export

        public void WriteTemplate(string path, ImportFileFormat format)
        {
            var rows = new List<IReadOnlyList<string>>
            {
                EmployeeImportColumns.All.Select(c => c.Example).ToList()
            };
            Write(path, format, new ImportTable(HeaderRow(), rows));
        }

        public void Export(long companyId, string path, ImportFileFormat format)
        {
            var rows = new List<IReadOnlyList<string>>();
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                foreach (Employee e in uow.Employees.GetByCompany(companyId, includeInactive: true))
                {
                    rows.Add(EmployeeImportColumns.All.Select(c => EmployeeImportColumns.Cell(e, c.Key) ?? string.Empty).ToList());
                }
            }
            Write(path, format, new ImportTable(HeaderRow(), rows));
        }

        private static List<string> HeaderRow() => EmployeeImportColumns.All.Select(c => c.Header).ToList();

        // ------------------------------------------------------------------ read

        public ImportTable Read(string path)
        {
            Guard.AgainstNull(path, nameof(path));
            return IsCsv(path) ? ReadCsv(path) : ReadXlsx(path);
        }

        private static bool IsCsv(string path) =>
            path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

        private static void Write(string path, ImportFileFormat format, ImportTable table)
        {
            if (format == ImportFileFormat.Csv) WriteCsv(path, table);
            else WriteXlsx(path, table);
        }

        // ------------------------------------------------------------------ preview

        public ImportPreview BuildPreview(long companyId, ImportTable table, IReadOnlyDictionary<string, string> mapping)
        {
            Guard.AgainstNull(table, nameof(table));

            // Resolve each column to a source index (explicit mapping first, then auto by header).
            var index = new Dictionary<string, int>();
            var missing = new List<string>();
            foreach (ImportColumn col in EmployeeImportColumns.All)
            {
                int i = ResolveColumn(col, table.Headers, mapping);
                if (i >= 0) index[col.Key] = i;
                else if (col.Required) missing.Add(col.Header);
            }

            var rowPreviews = new List<ImportRowPreview>();
            var allIssues = new List<ImportIssue>();

            if (missing.Count > 0)
            {
                // Can't meaningfully validate rows without the required columns — report and stop here.
                return new ImportPreview(rowPreviews, allIssues, missing);
            }

            HashSet<string> existingKeys = LoadExistingKeys(companyId);
            var seenInFile = new HashSet<string>(StringComparer.Ordinal);

            for (int r = 0; r < table.Rows.Count; r++)
            {
                int rowNumber = r + 2; // header is row 1
                IReadOnlyList<string> cells = table.Rows[r];
                if (IsBlankRow(cells)) continue; // tolerate trailing/blank lines

                var issues = new List<ImportIssue>();
                var e = new Employee { CompanyId = companyId, IsActive = true };

                foreach (ImportColumn col in EmployeeImportColumns.All)
                {
                    string raw = index.TryGetValue(col.Key, out int ci) && ci < cells.Count ? cells[ci] : string.Empty;
                    EmployeeImportColumns.Apply(e, col.Key, raw, rowNumber, col.Header, issues);
                }

                // Same rules as manual entry — nothing stricter.
                foreach (ValidationMessage m in _validator.Validate(e).Errors)
                {
                    issues.Add(new ImportIssue(rowNumber, ColumnHeaderFor(m.MemberName), BilingualValidation(m)));
                }

                string name = (e.LastNameFr + " " + e.FirstNameFr).Trim();
                ImportRowStatus status;
                if (issues.Count > 0)
                {
                    status = ImportRowStatus.Error;
                }
                else
                {
                    string key = DuplicateKey(e);
                    if (existingKeys.Contains(key) || !seenInFile.Add(key))
                    {
                        status = ImportRowStatus.Duplicate;
                    }
                    else
                    {
                        status = ImportRowStatus.New;
                    }
                }

                allIssues.AddRange(issues);
                rowPreviews.Add(new ImportRowPreview(rowNumber, name, status, issues));
            }

            return new ImportPreview(rowPreviews, allIssues, missing);
        }

        // ------------------------------------------------------------------ commit (atomic)

        public Result<ImportSummary> Commit(long companyId, ImportTable table, IReadOnlyDictionary<string, string> mapping,
            DuplicateResolution resolution, string fileName)
        {
            // Re-validate exactly what the preview showed; refuse on any error.
            ImportPreview preview = BuildPreview(companyId, table, mapping);
            if (preview.MissingColumns.Count > 0)
            {
                return Result.Fail<ImportSummary>(
                    "Colonnes obligatoires manquantes : " + string.Join(", ", preview.MissingColumns) +
                    " / أعمدة إلزامية ناقصة.", "Import_MissingColumns");
            }
            if (preview.ErrorCount > 0)
            {
                return Result.Fail<ImportSummary>(
                    "Le fichier contient " + preview.ErrorCount + " ligne(s) en erreur — rien n'a été importé. " +
                    "الملف يحتوي على أسطر بها أخطاء — لم يتم استيراد أي شيء.", "Import_HasErrors");
            }
            if (resolution == DuplicateResolution.Stop && preview.DuplicateCount > 0)
            {
                return Result.Fail<ImportSummary>(
                    "Le fichier contient " + preview.DuplicateCount + " doublon(s) — importation arrêtée. " +
                    "الملف يحتوي على تكرارات — تم إيقاف الاستيراد.", "Import_HasDuplicates");
            }
            if (preview.NewCount == 0 && preview.DuplicateCount == 0)
            {
                // Genuinely nothing actionable (empty file). All-duplicate + SkipDuplicates is NOT an
                // error — it legitimately creates nothing (e.g. re-importing an export), handled below.
                return Result.Fail<ImportSummary>("Aucun employé à importer. / لا يوجد موظف للاستيراد.", "Import_Nothing");
            }

            // Rebuild the employees to insert (the New rows only) and write them in ONE transaction.
            var index = new Dictionary<string, int>();
            foreach (ImportColumn col in EmployeeImportColumns.All)
            {
                int i = ResolveColumn(col, table.Headers, mapping);
                if (i >= 0) index[col.Key] = i;
            }

            HashSet<string> existingKeys = LoadExistingKeys(companyId);
            var seenInFile = new HashSet<string>(StringComparer.Ordinal);
            int created = 0, skipped = 0;

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (!uow.Companies.ExistsById(companyId))
                {
                    return Result.Fail<ImportSummary>("Entreprise introuvable.", ErrorCodes.EmployeeCompanyNotFound);
                }

                uow.BeginTransaction();
                try
                {
                    for (int r = 0; r < table.Rows.Count; r++)
                    {
                        IReadOnlyList<string> cells = table.Rows[r];
                        if (IsBlankRow(cells)) continue;

                        var issues = new List<ImportIssue>();
                        var e = new Employee { CompanyId = companyId, IsActive = true };
                        foreach (ImportColumn col in EmployeeImportColumns.All)
                        {
                            string raw = index.TryGetValue(col.Key, out int ci) && ci < cells.Count ? cells[ci] : string.Empty;
                            EmployeeImportColumns.Apply(e, col.Key, raw, r + 2, col.Header, issues);
                        }
                        if (issues.Count > 0)
                        {
                            uow.Rollback();
                            return Result.Fail<ImportSummary>("Erreur de validation à la ligne " + (r + 2) + ".", "Import_HasErrors");
                        }

                        string key = DuplicateKey(e);
                        if (existingKeys.Contains(key) || !seenInFile.Add(key))
                        {
                            skipped++; // SkipDuplicates (Stop was already refused above)
                            continue;
                        }

                        // Company is forced to the active company — never from the file.
                        e.CompanyId = companyId;
                        Result<long> inserted = EmployeeCreation.InsertValidated(uow, e, _validator, Audit);
                        if (inserted.IsFailure)
                        {
                            uow.Rollback();
                            return Result.Fail<ImportSummary>(inserted.Error, inserted.ErrorCode);
                        }

                        // Draft contract, exactly as manual entry (EmployeeEditViewModel) does — same transaction.
                        Result<long> contract = EmployeeCreation.InsertDraftContract(uow, inserted.Value, e);
                        if (contract.IsFailure)
                        {
                            uow.Rollback();
                            return Result.Fail<ImportSummary>(contract.Error, contract.ErrorCode);
                        }

                        created++;
                    }

                    uow.Commit();
                }
                catch (Exception)
                {
                    uow.Rollback();
                    return Result.Fail<ImportSummary>("Erreur lors de l'importation — rien n'a été écrit. / خطأ أثناء الاستيراد.", "Import_Failed");
                }
            }

            Audit.Record("EmployeeImport", companyId, AuditAction.Created,
                "Import de " + created + " employé(s)" + (skipped > 0 ? " (" + skipped + " doublon(s) ignoré(s))" : "") +
                " depuis « " + (fileName ?? "?") + " »",
                null, created.ToString(Inv));

            return Result.Ok(new ImportSummary(created, skipped));
        }

        // ------------------------------------------------------------------ helpers

        private HashSet<string> LoadExistingKeys(long companyId)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                foreach (Employee e in uow.Employees.GetByCompany(companyId, includeInactive: true))
                {
                    keys.Add(DuplicateKey(e));
                }
            }
            return keys;
        }

        /// <summary>A person is the same when the NSS matches (digits only); failing that, when the
        /// French last name + first name + birth date all match. Case-insensitive on names.</summary>
        private static string DuplicateKey(Employee e)
        {
            string nss = new string((e.Nss ?? string.Empty).Where(char.IsDigit).ToArray());
            if (nss.Length > 0) return "N:" + nss;
            return "P:" + (e.LastNameFr ?? string.Empty).Trim().ToLowerInvariant() + "|"
                        + (e.FirstNameFr ?? string.Empty).Trim().ToLowerInvariant() + "|"
                        + (e.BirthDate.HasValue ? e.BirthDate.Value.ToString("yyyy-MM-dd", Inv) : string.Empty);
        }

        private static int ResolveColumn(ImportColumn col, IReadOnlyList<string> headers, IReadOnlyDictionary<string, string> mapping)
        {
            if (mapping != null && mapping.TryGetValue(col.Key, out string mapped) && !string.IsNullOrWhiteSpace(mapped))
            {
                return IndexOfHeader(headers, mapped);
            }
            // Auto: accept the combined bilingual header, either language alone, or the field key.
            foreach (string candidate in new[] { col.Header, col.HeaderFr, col.HeaderAr, col.Key })
            {
                int i = IndexOfHeader(headers, candidate);
                if (i >= 0) return i;
            }
            return -1;
        }

        private static int IndexOfHeader(IReadOnlyList<string> headers, string target)
        {
            string t = Normalize(target);
            for (int i = 0; i < headers.Count; i++)
            {
                if (Normalize(headers[i]) == t) return i;
            }
            return -1;
        }

        private static string Normalize(string s) =>
            (s ?? string.Empty).Trim().Replace("  ", " ").ToLowerInvariant();

        private static bool IsBlankRow(IReadOnlyList<string> cells) =>
            cells == null || cells.All(c => string.IsNullOrWhiteSpace(c));

        private static string ColumnHeaderFor(string memberName)
        {
            ImportColumn c = EmployeeImportColumns.ByKey(memberName);
            return c != null ? c.Header : (string.IsNullOrEmpty(memberName) ? "—" : memberName);
        }

        private static string BilingualValidation(ValidationMessage m)
        {
            switch (m.Code)
            {
                case ErrorCodes.EmployeeLastNameRequired: return "Le nom est obligatoire / اللقب إجباري";
                case ErrorCodes.EmployeeFirstNameRequired: return "Le prénom est obligatoire / الاسم إجباري";
                case ErrorCodes.EmployeeBaseSalaryInvalid: return "Le salaire de base ne peut pas être négatif / الأجر القاعدي لا يمكن أن يكون سالبًا";
                case ErrorCodes.EmployeeHireDateRequired: return "La date de recrutement est obligatoire / تاريخ التوظيف إجباري";
                case ErrorCodes.EmployeeHireDateInFuture: return "La date de recrutement ne peut pas être dans le futur / تاريخ التوظيف لا يمكن أن يكون في المستقبل";
                case ErrorCodes.EmployeeExitBeforeHire: return "La date de sortie ne peut pas précéder le recrutement / تاريخ المغادرة قبل التوظيف";
                case ErrorCodes.EmployeeBirthDateInvalid: return "Date de naissance invalide / تاريخ ميلاد غير صالح";
                default: return (m.DefaultMessage ?? "Valeur invalide") + " / قيمة غير صالحة";
            }
        }

        // ------------------------------------------------------------------ xlsx (ClosedXML)

        private static ImportTable ReadXlsx(string path)
        {
            using (var wb = new XLWorkbook(path))
            {
                IXLWorksheet ws = wb.Worksheets.FirstOrDefault();
                IXLRange used = ws?.RangeUsed();
                if (ws == null || used == null) return new ImportTable(new List<string>(), new List<IReadOnlyList<string>>());

                int firstRow = used.FirstRow().RowNumber();
                int lastRow = used.LastRow().RowNumber();
                int firstCol = used.FirstColumn().ColumnNumber();
                int lastCol = used.LastColumn().ColumnNumber();

                var headers = new List<string>();
                for (int c = firstCol; c <= lastCol; c++) headers.Add(CellString(ws.Cell(firstRow, c)));

                var rows = new List<IReadOnlyList<string>>();
                for (int r = firstRow + 1; r <= lastRow; r++)
                {
                    var row = new List<string>();
                    for (int c = firstCol; c <= lastCol; c++) row.Add(CellString(ws.Cell(r, c)));
                    rows.Add(row);
                }
                return new ImportTable(headers, rows);
            }
        }

        private static string CellString(IXLCell cell)
        {
            if (cell == null) return string.Empty;
            try
            {
                switch (cell.DataType)
                {
                    case XLDataType.DateTime: return cell.GetDateTime().ToString("yyyy-MM-dd", Inv);
                    case XLDataType.Number: return cell.GetDouble().ToString(Inv);
                    case XLDataType.Boolean: return cell.GetBoolean() ? "Oui" : "Non";
                    default: return (cell.GetString() ?? string.Empty).Trim();
                }
            }
            catch
            {
                return (cell.GetString() ?? string.Empty).Trim();
            }
        }

        private static void WriteXlsx(string path, ImportTable table)
        {
            using (var wb = new XLWorkbook())
            {
                IXLWorksheet ws = wb.AddWorksheet("Employés");
                for (int c = 0; c < table.Headers.Count; c++)
                {
                    IXLCell cell = ws.Cell(1, c + 1);
                    cell.Value = table.Headers[c];
                    cell.Style.Font.Bold = true;
                }
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    for (int c = 0; c < table.Rows[r].Count; c++)
                    {
                        IXLCell cell = ws.Cell(r + 2, c + 1);
                        cell.Style.NumberFormat.Format = "@"; // text — never let Excel reinterpret NSS/dates
                        cell.Value = table.Rows[r][c] ?? string.Empty;
                    }
                }
                try { ws.Columns().AdjustToContents(); } catch { /* cosmetic */ }
                wb.SaveAs(path);
            }
        }

        // ------------------------------------------------------------------ csv

        private static ImportTable ReadCsv(string path)
        {
            string text = File.ReadAllText(path, DetectEncoding(path));
            List<List<string>> records = ParseCsv(text);
            if (records.Count == 0) return new ImportTable(new List<string>(), new List<IReadOnlyList<string>>());
            var headers = records[0].Select(h => (h ?? string.Empty).Trim()).ToList();
            var rows = records.Skip(1).Select(r => (IReadOnlyList<string>)r).ToList();
            return new ImportTable(headers, rows);
        }

        private static Encoding DetectEncoding(string path)
        {
            byte[] bom = new byte[3];
            using (FileStream fs = File.OpenRead(path)) { fs.Read(bom, 0, 3); }
            if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return new UTF8Encoding(true);
            return new UTF8Encoding(false);
        }

        private static void WriteCsv(string path, ImportTable table)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", table.Headers.Select(CsvField)));
            foreach (IReadOnlyList<string> row in table.Rows)
            {
                sb.AppendLine(string.Join(",", row.Select(CsvField)));
            }
            // UTF-8 WITH BOM so Excel opens the Arabic headers/values correctly.
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string CsvField(string value)
        {
            string v = value ?? string.Empty;
            if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            {
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            }
            return v;
        }

        private static List<List<string>> ParseCsv(string text)
        {
            var records = new List<List<string>>();
            var field = new StringBuilder();
            var record = new List<string>();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(ch);
                }
                else
                {
                    switch (ch)
                    {
                        case '"': inQuotes = true; break;
                        case ',': record.Add(field.ToString()); field.Clear(); break;
                        case '\r': break;
                        case '\n':
                            record.Add(field.ToString()); field.Clear();
                            records.Add(record); record = new List<string>();
                            break;
                        default: field.Append(ch); break;
                    }
                }
            }
            // last field/record (no trailing newline)
            if (field.Length > 0 || record.Count > 0)
            {
                record.Add(field.ToString());
                records.Add(record);
            }
            return records;
        }
    }
}
