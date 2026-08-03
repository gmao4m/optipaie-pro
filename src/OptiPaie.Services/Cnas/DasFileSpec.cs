namespace OptiPaie.Services.Cnas
{
    /// <summary>Alignment of a fixed-width DAS field within its column.</summary>
    public enum DasAlign
    {
        Left,
        Right
    }

    /// <summary>
    /// One fixed-width field of a DAS record: where it sits (0-based offset), how wide, and how
    /// its value is aligned. Pure data — it carries no formatting logic. Padding is always with
    /// spaces (the DAS format never uses leading zeros).
    /// </summary>
    public sealed class DasFieldSpec
    {
        public DasFieldSpec(string name, int offset, int length, DasAlign align)
        {
            Name = name;
            Offset = offset;
            Length = length;
            Align = align;
        }

        public string Name { get; }
        public int Offset { get; }
        public int Length { get; }
        public DasAlign Align { get; }
    }

    /// <summary>
    /// THE single, readable spec of the CNAS DAS text files — derived by binary analysis of two
    /// real 2021 files (employer 1639259938). Everything here is DATA: offsets, lengths, alignment,
    /// line lengths, and the two constants whose role is unknown ("à confirmer"). Correcting the
    /// format means editing THIS file only — never the encoder/builder logic.
    /// <para>
    /// General rules (enforced by <see cref="DasLineBuilder"/> / <see cref="DasAsciiWriter"/>):
    /// pure ASCII, no BOM; CR LF line endings; header = one 221-char line + a trailing CR LF;
    /// detail = 195-char lines joined by CR LF with NO trailing CR LF. Amounts are integer
    /// centimes, right-aligned, space-filled, never zero-padded, never truncated. Text is
    /// left-aligned. Dates are jjmmaaaa, or 8 spaces when absent.
    /// </para>
    /// </summary>
    public static class DasFileSpec
    {
        public const int HeaderLength = 221;
        public const int DetailLength = 195;

        /// <summary>Max width of a per-quarter salary field: 7 digits = 99 999,99 DA. Beyond → refuse.</summary>
        public const int QuarterSalaryLength = 7;

        /// <summary>Detail position 194 — value "0" on the reference lines, role unknown. À CONFIRMER.</summary>
        public const string EndIndicator = "0";

        /// <summary>Header type marker (position 10): "N" (normal) or "C". Reference file uses "N".</summary>
        public const string HeaderTypeNormal = "N";

        // ------------------------------------------------------------------ DÉTAIL (195)
        // Offsets 0-based. Positions not covered by any field stay spaces (filler): 101-103,
        // 115-117, 129-131, 143-145 (inter-quarter fillers), and 154-157.
        public static class Detail
        {
            public static readonly DasFieldSpec EmployerNumber = new DasFieldSpec("N° employeur", 0, 10, DasAlign.Left);
            public static readonly DasFieldSpec Year = new DasFieldSpec("Année", 10, 4, DasAlign.Left);
            public static readonly DasFieldSpec LineNumber = new DasFieldSpec("N° de ligne", 14, 6, DasAlign.Left);
            public static readonly DasFieldSpec Nss = new DasFieldSpec("N° sécurité sociale", 20, 12, DasAlign.Left);
            public static readonly DasFieldSpec LastName = new DasFieldSpec("Nom", 32, 25, DasAlign.Left);
            public static readonly DasFieldSpec FirstName = new DasFieldSpec("Prénom", 57, 25, DasAlign.Left);
            public static readonly DasFieldSpec BirthDate = new DasFieldSpec("Date de naissance", 82, 8, DasAlign.Left);

            public static readonly DasFieldSpec DurationT1 = new DasFieldSpec("Durée T1", 90, 3, DasAlign.Right);
            public static readonly DasFieldSpec UnitT1 = new DasFieldSpec("Unité T1", 93, 1, DasAlign.Left);
            public static readonly DasFieldSpec SalaryT1 = new DasFieldSpec("Salaire T1", 94, 7, DasAlign.Right);
            public static readonly DasFieldSpec DurationT2 = new DasFieldSpec("Durée T2", 104, 3, DasAlign.Right);
            public static readonly DasFieldSpec UnitT2 = new DasFieldSpec("Unité T2", 107, 1, DasAlign.Left);
            public static readonly DasFieldSpec SalaryT2 = new DasFieldSpec("Salaire T2", 108, 7, DasAlign.Right);
            public static readonly DasFieldSpec DurationT3 = new DasFieldSpec("Durée T3", 118, 3, DasAlign.Right);
            public static readonly DasFieldSpec UnitT3 = new DasFieldSpec("Unité T3", 121, 1, DasAlign.Left);
            public static readonly DasFieldSpec SalaryT3 = new DasFieldSpec("Salaire T3", 122, 7, DasAlign.Right);
            public static readonly DasFieldSpec DurationT4 = new DasFieldSpec("Durée T4", 132, 3, DasAlign.Right);
            public static readonly DasFieldSpec UnitT4 = new DasFieldSpec("Unité T4", 135, 1, DasAlign.Left);
            public static readonly DasFieldSpec SalaryT4 = new DasFieldSpec("Salaire T4", 136, 7, DasAlign.Right);

            public static readonly DasFieldSpec AnnualTotal = new DasFieldSpec("Total annuel", 146, 8, DasAlign.Right);
            public static readonly DasFieldSpec EntryDate = new DasFieldSpec("Date d'entrée", 158, 8, DasAlign.Left);
            public static readonly DasFieldSpec ExitDate = new DasFieldSpec("Date de sortie", 166, 8, DasAlign.Left);
            public static readonly DasFieldSpec Observation = new DasFieldSpec("Observation", 174, 20, DasAlign.Left);
            public static readonly DasFieldSpec End = new DasFieldSpec("Indicateur de fin", 194, 1, DasAlign.Left);

            public static readonly DasFieldSpec[] All =
            {
                EmployerNumber, Year, LineNumber, Nss, LastName, FirstName, BirthDate,
                DurationT1, UnitT1, SalaryT1, DurationT2, UnitT2, SalaryT2,
                DurationT3, UnitT3, SalaryT3, DurationT4, UnitT4, SalaryT4,
                AnnualTotal, EntryDate, ExitDate, Observation, End
            };
        }

        // ------------------------------------------------------------------ ENTÊTE (221)
        // Filler/anomaly positions left as spaces: 193 (single-space anomaly between T4 and the
        // annual total) and 216-220. The dénomination zone (20-128, 109 chars) is empty in the
        // reference files — its default value is 109 spaces (parameterizable, see the builder).
        public static class Header
        {
            public static readonly DasFieldSpec EmployerNumber = new DasFieldSpec("N° employeur", 0, 10, DasAlign.Left);
            public static readonly DasFieldSpec Type = new DasFieldSpec("Type", 10, 1, DasAlign.Left);
            public static readonly DasFieldSpec Year = new DasFieldSpec("Année", 11, 4, DasAlign.Left);
            public static readonly DasFieldSpec CentrePayeur = new DasFieldSpec("Centre payeur", 15, 5, DasAlign.Left);
            public static readonly DasFieldSpec Denomination = new DasFieldSpec("Dénomination", 20, 109, DasAlign.Left);

            public static readonly DasFieldSpec TotalT1 = new DasFieldSpec("Total T1", 129, 16, DasAlign.Right);
            public static readonly DasFieldSpec TotalT2 = new DasFieldSpec("Total T2", 145, 16, DasAlign.Right);
            public static readonly DasFieldSpec TotalT3 = new DasFieldSpec("Total T3", 161, 16, DasAlign.Right);
            public static readonly DasFieldSpec TotalT4 = new DasFieldSpec("Total T4", 177, 16, DasAlign.Right);
            public static readonly DasFieldSpec AnnualTotal = new DasFieldSpec("Total annuel", 194, 16, DasAlign.Right);
            public static readonly DasFieldSpec WorkerCount = new DasFieldSpec("Nombre de travailleurs", 210, 6, DasAlign.Right);

            public static readonly DasFieldSpec[] All =
            {
                EmployerNumber, Type, Year, CentrePayeur, Denomination,
                TotalT1, TotalT2, TotalT3, TotalT4, AnnualTotal, WorkerCount
            };
        }
    }
}
