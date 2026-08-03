using System.Collections.Generic;
using OptiPaie.Core.Dtos;

namespace OptiPaie.Services.Cnas
{
    /// <summary>Header configuration that is not part of the aggregated data (config, not payroll).</summary>
    public sealed class DasHeaderOptions
    {
        public DasHeaderOptions(string type, string centrePayeur, string denomination)
        {
            Type = type;
            CentrePayeur = centrePayeur;
            Denomination = denomination;
        }

        /// <summary>"N" (normal) or "C". Reference file uses "N".</summary>
        public string Type { get; }

        /// <summary>Centre payeur (header 15-19). Reference file: "16000". Parameterizable per company.</summary>
        public string CentrePayeur { get; }

        /// <summary>Dénomination zone (header 20-128). Empty in the reference files; parameterizable.</summary>
        public string Denomination { get; }
    }

    /// <summary>The two DAS files as bytes, plus the built lines for inspection.</summary>
    public sealed class DasFiles
    {
        public DasFiles(byte[] headerBytes, byte[] detailBytes, string headerLine, IReadOnlyList<string> detailLines)
        {
            HeaderBytes = headerBytes;
            DetailBytes = detailBytes;
            HeaderLine = headerLine;
            DetailLines = detailLines;
        }

        public byte[] HeaderBytes { get; }
        public byte[] DetailBytes { get; }
        public string HeaderLine { get; }
        public IReadOnlyList<string> DetailLines { get; }
    }

    /// <summary>
    /// Turns an aggregated <see cref="CnasDasReport"/> into the two fixed-width DAS files, placing
    /// every value through <see cref="DasFileSpec"/> + <see cref="DasLineBuilder"/> and serializing
    /// with <see cref="DasAsciiWriter"/> (pure ASCII, no BOM, the header/detail CR LF rules). It
    /// only lays out already-computed figures — no aggregation, no rate, no rounding of amounts.
    /// </summary>
    public sealed class DasFileBuilder
    {
        public string BuildDetailLine(string employer, int year, int lineNumber, CnasDasEmployee e)
        {
            var b = new DasLineBuilder(DasFileSpec.DetailLength)
                .Put(DasFileSpec.Detail.EmployerNumber, employer)
                .Put(DasFileSpec.Detail.Year, DasFormat.Int(year))
                .Put(DasFileSpec.Detail.LineNumber, DasFormat.Int(lineNumber))
                .Put(DasFileSpec.Detail.Nss, e.Nss)
                .Put(DasFileSpec.Detail.LastName, e.LastName)
                .Put(DasFileSpec.Detail.FirstName, e.FirstName)
                .Put(DasFileSpec.Detail.BirthDate, DasFormat.Date(e.BirthDate));

            PutQuarter(b, DasFileSpec.Detail.DurationT1, DasFileSpec.Detail.UnitT1, DasFileSpec.Detail.SalaryT1, e.Quarters[0]);
            PutQuarter(b, DasFileSpec.Detail.DurationT2, DasFileSpec.Detail.UnitT2, DasFileSpec.Detail.SalaryT2, e.Quarters[1]);
            PutQuarter(b, DasFileSpec.Detail.DurationT3, DasFileSpec.Detail.UnitT3, DasFileSpec.Detail.SalaryT3, e.Quarters[2]);
            PutQuarter(b, DasFileSpec.Detail.DurationT4, DasFileSpec.Detail.UnitT4, DasFileSpec.Detail.SalaryT4, e.Quarters[3]);

            return b
                .Put(DasFileSpec.Detail.AnnualTotal, DasFormat.Amount(e.AnnualSalary))
                .Put(DasFileSpec.Detail.EntryDate, DasFormat.Date(e.HireDate))
                .Put(DasFileSpec.Detail.ExitDate, DasFormat.Date(e.ExitDate))
                .Put(DasFileSpec.Detail.End, DasFileSpec.EndIndicator)
                .Build();
        }

        private static void PutQuarter(DasLineBuilder b, DasFieldSpec duration, DasFieldSpec unit, DasFieldSpec salary, CnasDasQuarter q)
        {
            // Every quarter is written, including inactive ones (0 duration, unit H, 0 salary),
            // exactly as the reference files do.
            b.Put(duration, DasFormat.Int(q.Hours))
             .Put(unit, DasFileSpec.DurationUnit)
             .Put(salary, DasFormat.Amount(q.Salary));
        }

        public string BuildHeaderLine(CnasDasReport r, DasHeaderOptions options)
        {
            return new DasLineBuilder(DasFileSpec.HeaderLength)
                .Put(DasFileSpec.Header.EmployerNumber, r.EmployerNumber)
                .Put(DasFileSpec.Header.Type, options.Type)
                .Put(DasFileSpec.Header.Year, DasFormat.Int(r.Year))
                .Put(DasFileSpec.Header.CentrePayeur, options.CentrePayeur)
                .Put(DasFileSpec.Header.Denomination, options.Denomination ?? string.Empty)
                .Put(DasFileSpec.Header.TotalT1, DasFormat.Amount(r.QuarterTotals[0]))
                .Put(DasFileSpec.Header.TotalT2, DasFormat.Amount(r.QuarterTotals[1]))
                .Put(DasFileSpec.Header.TotalT3, DasFormat.Amount(r.QuarterTotals[2]))
                .Put(DasFileSpec.Header.TotalT4, DasFormat.Amount(r.QuarterTotals[3]))
                .Put(DasFileSpec.Header.AnnualTotal, DasFormat.Amount(r.AnnualTotal))
                .Put(DasFileSpec.Header.WorkerCount, DasFormat.Int(r.WorkerCount))
                .Build();
        }

        public DasFiles Build(CnasDasReport r, DasHeaderOptions options)
        {
            var detailLines = new List<string>(r.Employees.Count);
            int lineNumber = 1;
            foreach (CnasDasEmployee e in r.Employees)
            {
                detailLines.Add(BuildDetailLine(r.EmployerNumber, r.Year, lineNumber, e));
                lineNumber++;
            }

            string headerLine = BuildHeaderLine(r, options);
            return new DasFiles(
                DasAsciiWriter.WriteHeader(headerLine),
                DasAsciiWriter.WriteDetail(detailLines),
                headerLine, detailLines);
        }
    }
}
