using System.Collections.Generic;

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
    /// Lays out fixed-width fields left-to-right, computing each field's offset from the running
    /// total so a single width change ripples through every downstream offset and the line length
    /// automatically. Padding gaps between fields are declared as <see cref="Filler"/> so they are
    /// visible and also shift correctly.
    /// </summary>
    internal sealed class DasLayout
    {
        private int _offset;
        private readonly List<DasFieldSpec> _fields = new List<DasFieldSpec>();

        public DasFieldSpec Field(string name, int width, DasAlign align)
        {
            var f = new DasFieldSpec(name, _offset, width, align);
            _offset += width;
            _fields.Add(f);
            return f;
        }

        public void Filler(int width) => _offset += width;

        /// <summary>Total line length = the running offset after every field and filler.</summary>
        public int Length => _offset;

        public DasFieldSpec[] Fields => _fields.ToArray();
    }

    /// <summary>
    /// The DAS detail record (one employee), laid out from <see cref="DasFileSpec.Config"/>. Offsets
    /// and the total <see cref="Length"/> are COMPUTED, so widening a field (e.g. the quarterly salary)
    /// shifts everything after it and grows the line — no offset is hand-written.
    /// </summary>
    public sealed class DasDetailLayout
    {
        public DasFieldSpec EmployerNumber { get; }
        public DasFieldSpec Year { get; }
        public DasFieldSpec LineNumber { get; }
        public DasFieldSpec Nss { get; }
        public DasFieldSpec LastName { get; }
        public DasFieldSpec FirstName { get; }
        public DasFieldSpec BirthDate { get; }

        public DasFieldSpec DurationT1 { get; }
        public DasFieldSpec UnitT1 { get; }
        public DasFieldSpec SalaryT1 { get; }
        public DasFieldSpec DurationT2 { get; }
        public DasFieldSpec UnitT2 { get; }
        public DasFieldSpec SalaryT2 { get; }
        public DasFieldSpec DurationT3 { get; }
        public DasFieldSpec UnitT3 { get; }
        public DasFieldSpec SalaryT3 { get; }
        public DasFieldSpec DurationT4 { get; }
        public DasFieldSpec UnitT4 { get; }
        public DasFieldSpec SalaryT4 { get; }

        public DasFieldSpec AnnualTotal { get; }
        public DasFieldSpec EntryDate { get; }
        public DasFieldSpec ExitDate { get; }
        public DasFieldSpec Observation { get; }
        public DasFieldSpec End { get; }

        public DasFieldSpec[] All { get; }
        public int Length { get; }

        /// <param name="quarterSalaryWidth">Width of each per-quarter salary field — the single value
        /// whose change widens all four salary columns and shifts every field after them.</param>
        public DasDetailLayout(int quarterSalaryWidth)
        {
            var l = new DasLayout();

            EmployerNumber = l.Field("N° employeur", DasFileSpec.Config.EmployerNumberWidth, DasAlign.Left);
            Year = l.Field("Année", DasFileSpec.Config.YearWidth, DasAlign.Left);
            LineNumber = l.Field("N° de ligne", DasFileSpec.Config.LineNumberWidth, DasAlign.Left);
            Nss = l.Field("N° sécurité sociale", DasFileSpec.Config.NssWidth, DasAlign.Left);
            LastName = l.Field("Nom", DasFileSpec.Config.NameWidth, DasAlign.Left);
            FirstName = l.Field("Prénom", DasFileSpec.Config.NameWidth, DasAlign.Left);
            BirthDate = l.Field("Date de naissance", DasFileSpec.Config.DateWidth, DasAlign.Left);

            DurationT1 = l.Field("Durée T1", DasFileSpec.Config.DurationWidth, DasAlign.Right);
            UnitT1 = l.Field("Unité T1", DasFileSpec.Config.DurationUnitWidth, DasAlign.Left);
            SalaryT1 = l.Field("Salaire T1", quarterSalaryWidth, DasAlign.Right);
            l.Filler(DasFileSpec.Config.InterQuarterFillerWidth);
            DurationT2 = l.Field("Durée T2", DasFileSpec.Config.DurationWidth, DasAlign.Right);
            UnitT2 = l.Field("Unité T2", DasFileSpec.Config.DurationUnitWidth, DasAlign.Left);
            SalaryT2 = l.Field("Salaire T2", quarterSalaryWidth, DasAlign.Right);
            l.Filler(DasFileSpec.Config.InterQuarterFillerWidth);
            DurationT3 = l.Field("Durée T3", DasFileSpec.Config.DurationWidth, DasAlign.Right);
            UnitT3 = l.Field("Unité T3", DasFileSpec.Config.DurationUnitWidth, DasAlign.Left);
            SalaryT3 = l.Field("Salaire T3", quarterSalaryWidth, DasAlign.Right);
            l.Filler(DasFileSpec.Config.InterQuarterFillerWidth);
            DurationT4 = l.Field("Durée T4", DasFileSpec.Config.DurationWidth, DasAlign.Right);
            UnitT4 = l.Field("Unité T4", DasFileSpec.Config.DurationUnitWidth, DasAlign.Left);
            SalaryT4 = l.Field("Salaire T4", quarterSalaryWidth, DasAlign.Right);
            l.Filler(DasFileSpec.Config.InterQuarterFillerWidth);

            AnnualTotal = l.Field("Total annuel", DasFileSpec.Config.DetailAnnualTotalWidth, DasAlign.Right);
            l.Filler(DasFileSpec.Config.DetailAnnualTrailingFillerWidth);
            EntryDate = l.Field("Date d'entrée", DasFileSpec.Config.DateWidth, DasAlign.Left);
            ExitDate = l.Field("Date de sortie", DasFileSpec.Config.DateWidth, DasAlign.Left);
            Observation = l.Field("Observation", DasFileSpec.Config.ObservationWidth, DasAlign.Left);
            End = l.Field("Indicateur de fin", DasFileSpec.Config.EndIndicatorWidth, DasAlign.Left);

            All = new[]
            {
                EmployerNumber, Year, LineNumber, Nss, LastName, FirstName, BirthDate,
                DurationT1, UnitT1, SalaryT1, DurationT2, UnitT2, SalaryT2,
                DurationT3, UnitT3, SalaryT3, DurationT4, UnitT4, SalaryT4,
                AnnualTotal, EntryDate, ExitDate, Observation, End
            };
            Length = l.Length;
        }
    }

    /// <summary>
    /// The DAS header record (one per file), laid out from <see cref="DasFileSpec.Config"/>. Offsets
    /// and <see cref="Length"/> are computed. The header's amount fields are the company-wide TOTALS
    /// (wider than a per-employee quarter salary), so they do NOT depend on the quarterly salary width.
    /// </summary>
    public sealed class DasHeaderLayout
    {
        public DasFieldSpec EmployerNumber { get; }
        public DasFieldSpec Type { get; }
        public DasFieldSpec Year { get; }
        public DasFieldSpec CentrePayeur { get; }
        public DasFieldSpec Denomination { get; }
        public DasFieldSpec TotalT1 { get; }
        public DasFieldSpec TotalT2 { get; }
        public DasFieldSpec TotalT3 { get; }
        public DasFieldSpec TotalT4 { get; }
        public DasFieldSpec AnnualTotal { get; }
        public DasFieldSpec WorkerCount { get; }

        public DasFieldSpec[] All { get; }
        public int Length { get; }

        public DasHeaderLayout()
        {
            var l = new DasLayout();

            EmployerNumber = l.Field("N° employeur", DasFileSpec.Config.EmployerNumberWidth, DasAlign.Left);
            Type = l.Field("Type", DasFileSpec.Config.TypeWidth, DasAlign.Left);
            Year = l.Field("Année", DasFileSpec.Config.YearWidth, DasAlign.Left);
            CentrePayeur = l.Field("Centre payeur", DasFileSpec.Config.CentrePayeurWidth, DasAlign.Left);
            Denomination = l.Field("Dénomination", DasFileSpec.Config.DenominationWidth, DasAlign.Left);

            TotalT1 = l.Field("Total T1", DasFileSpec.Config.HeaderTotalWidth, DasAlign.Right);
            TotalT2 = l.Field("Total T2", DasFileSpec.Config.HeaderTotalWidth, DasAlign.Right);
            TotalT3 = l.Field("Total T3", DasFileSpec.Config.HeaderTotalWidth, DasAlign.Right);
            TotalT4 = l.Field("Total T4", DasFileSpec.Config.HeaderTotalWidth, DasAlign.Right);
            l.Filler(DasFileSpec.Config.HeaderTotalToAnnualFillerWidth); // single-space anomaly between T4 and the annual total
            AnnualTotal = l.Field("Total annuel", DasFileSpec.Config.HeaderTotalWidth, DasAlign.Right);
            WorkerCount = l.Field("Nombre de travailleurs", DasFileSpec.Config.WorkerCountWidth, DasAlign.Right);
            l.Filler(DasFileSpec.Config.HeaderTrailingFillerWidth);

            All = new[]
            {
                EmployerNumber, Type, Year, CentrePayeur, Denomination,
                TotalT1, TotalT2, TotalT3, TotalT4, AnnualTotal, WorkerCount
            };
            Length = l.Length;
        }
    }

    /// <summary>
    /// THE single, readable spec of the CNAS DAS text files — derived by binary analysis of two real
    /// 2021 files (employer 1639259938). Everything here is DATA; correcting the format means editing
    /// <see cref="Config"/> only, never the encoder/builder logic.
    /// <para>
    /// General rules (enforced by <see cref="DasLineBuilder"/> / <see cref="DasAsciiWriter"/>): pure
    /// ASCII, no BOM; CR LF line endings; header = one line + a trailing CR LF; detail = lines joined
    /// by CR LF with NO trailing CR LF. Amounts are integer centimes, right-aligned, space-filled,
    /// never zero-padded, NEVER truncated (a value too wide for its field is REFUSED — the guard is
    /// kept). Text is left-aligned. Dates are jjmmaaaa, or 8 spaces when absent.
    /// </para>
    /// <para>
    /// ⚠ EVERYTHING IN <see cref="Config"/> IS UNCONFIRMED against the CURRENT CNAS portal. The whole
    /// format was reconstructed from two 2021 files of a single employer; it has never been confronted
    /// with a live deposit. Each constant below records what we assume and why. The FIRST accepted (or
    /// rejected) real deposit is the moment to verify them all in one pass — and correcting any of them
    /// is a ONE-VALUE edit here, with the encoder, offsets and line lengths adapting automatically.
    /// </para>
    /// </summary>
    public static class DasFileSpec
    {
        /// <summary>
        /// The single named configuration block for the DAS format. Change a value HERE and nothing
        /// else needs editing — offsets and line lengths are computed from these widths. Every entry
        /// is « à confirmer au 1er dépôt réel » (unconfirmed — pending a real deposit); the note on
        /// each says whether it was proven by the 2021 files or is a pure assumption.
        /// </summary>
        public static class Config
        {
            // ====================================================================================
            //  ⭐ THE ONE TO CHANGE FIRST — quarterly salary field width.
            // ====================================================================================
            /// <summary>
            /// Width of EACH per-quarter salary field, in characters (integer centimes). 7 ⇒ a cap of
            /// 99 999,99 DA per quarter and per employee (~33 000 DA/month over a full quarter): ANY
            /// employee above that has their DAS export REFUSED (never truncated).
            /// <para>ASSUME: the 2021 files used a 7-char field (demonstrated arithmetically — the 16
            /// zero salaries all sat on the right edge of a 7-char field, and the largest real amount,
            /// 84 000 DA/quarter, already filled 84% of it). We DO NOT KNOW whether the current portal
            /// widened it — our two files carry only modest salaries.</para>
            /// <para>STATUS: UNCONFIRMED — pending real deposit. This is the field most likely to hit a
            /// real customer. The day a deposit tells us the true width, change THIS number only: the
            /// four salary columns widen, every field after them shifts, the detail line length grows,
            /// and the export guard (<see cref="QuarterSalaryLength"/>) all follow automatically.</para>
            /// </summary>
            public const int QuarterSalaryWidth = 7;

            // ====================================================================================
            //  Detail record — other field widths (jours ouvrables of binary analysis, 2021 files).
            // ====================================================================================
            /// <summary>Employer number, 10 digits. ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int EmployerNumberWidth = 10;
            /// <summary>Year (aaaa), 4 digits. ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int YearWidth = 4;
            /// <summary>Line number, 6 digits. ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int LineNumberWidth = 6;
            /// <summary>N° sécurité sociale, 12 digits (key included in the string). ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int NssWidth = 12;
            /// <summary>Last name AND first name field width, 25 chars each. ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int NameWidth = 25;
            /// <summary>Date field (jjmmaaaa), 8 chars — birth / entry / exit. ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int DateWidth = 8;
            /// <summary>Per-quarter duration, 3 digits. ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int DurationWidth = 3;
            /// <summary>Per-quarter duration unit, 1 char (the letter after the duration). ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int DurationUnitWidth = 1;
            /// <summary>Filler between one quarter block and the next, 3 spaces. ASSUME: proven in the 2021 files (positions 101-103, 115-117, 129-131, 143-145). STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int InterQuarterFillerWidth = 3;
            /// <summary>Per-employee annual total (detail), 8 digits. ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int DetailAnnualTotalWidth = 8;
            /// <summary>Filler after the detail annual total, 4 spaces (positions 154-157). ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int DetailAnnualTrailingFillerWidth = 4;
            /// <summary>Observation free-text, 20 chars. ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int ObservationWidth = 20;
            /// <summary>End-of-line indicator, 1 char (see <see cref="EndIndicator"/>). ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int EndIndicatorWidth = 1;

            // ====================================================================================
            //  Header record — field widths.
            // ====================================================================================
            /// <summary>Header type marker, 1 char (see <see cref="HeaderTypeNormal"/>). ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int TypeWidth = 1;
            /// <summary>Centre payeur, 5 digits (2021 file: « 16000 »). ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int CentrePayeurWidth = 5;
            /// <summary>Dénomination / adresse zone, 109 chars — EMPTY (109 spaces) in the 2021 files; internal boundaries (where the name ends and the address begins) unknown. ASSUME: « vide = 109 espaces » is accepted. STATUS: UNCONFIRMED — pending real deposit.</summary>
            public const int DenominationWidth = 109;
            /// <summary>Header amount TOTAL width (each quarter total + the annual total), 16 digits. These are company-wide sums, independent of the per-employee quarterly salary width. ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int HeaderTotalWidth = 16;
            /// <summary>Filler between the T4 total and the annual total in the header, 1 space (« single-space anomaly » at position 193). ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int HeaderTotalToAnnualFillerWidth = 1;
            /// <summary>Nombre de travailleurs, 6 digits. ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int WorkerCountWidth = 6;
            /// <summary>Trailing filler at the end of the header line, 5 spaces (positions 216-220). ASSUME: proven in the 2021 files. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const int HeaderTrailingFillerWidth = 5;

            // ====================================================================================
            //  Format constants (non-width) — the literal marks written into the files.
            // ====================================================================================
            /// <summary>End-of-line indicator value (detail position 194). 2021 files: « 0 ». ROLE UNKNOWN (a status? a line type?). ASSUME: reproduce « 0 » verbatim because that is what the accepted file contained. STATUS: UNCONFIRMED — pending real deposit.</summary>
            public const string EndIndicator = "0";
            /// <summary>Duration unit written after each quarter duration. 2021 files: « H » (heures). ASSUME: heures. STATUS: UNCONFIRMED — pending real deposit (a centre could require « M » = mois).</summary>
            public const string DurationUnit = "H";
            /// <summary>Header type marker (position 10): « N » (normal) or « C ». 2021 file: « N ». ASSUME: « N » for a normal annual declaration. STATUS: à confirmer au 1er dépôt réel.</summary>
            public const string HeaderTypeNormal = "N";
        }

        // ------------------------------------------------------------------ back-compat aliases
        // Existing call sites (encoder, builder, export guard, tests) keep reading these names; each
        // now forwards to Config so there is a single source of truth for every value.

        /// <summary>Max width of a per-quarter salary field (see <see cref="Config.QuarterSalaryWidth"/>). Beyond → the export is refused.</summary>
        public static int QuarterSalaryLength => Config.QuarterSalaryWidth;

        /// <summary>Detail end-of-line indicator (see <see cref="Config.EndIndicator"/>).</summary>
        public static string EndIndicator => Config.EndIndicator;

        /// <summary>Per-quarter duration unit (see <see cref="Config.DurationUnit"/>).</summary>
        public static string DurationUnit => Config.DurationUnit;

        /// <summary>Header type marker for a normal declaration (see <see cref="Config.HeaderTypeNormal"/>).</summary>
        public static string HeaderTypeNormal => Config.HeaderTypeNormal;

        // ------------------------------------------------------------------ computed layouts
        /// <summary>The detail record layout, built from the configured widths (offsets computed).</summary>
        public static readonly DasDetailLayout Detail = new DasDetailLayout(Config.QuarterSalaryWidth);

        /// <summary>The header record layout, built from the configured widths (offsets computed).</summary>
        public static readonly DasHeaderLayout Header = new DasHeaderLayout();

        /// <summary>Total detail line length — COMPUTED from the layout, so it follows any width change.</summary>
        public static int DetailLength => Detail.Length;

        /// <summary>Total header line length — COMPUTED from the layout, so it follows any width change.</summary>
        public static int HeaderLength => Header.Length;
    }
}
