namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// An immutable, frozen rendering of a <see cref="Payslip"/>, kept for faithful
    /// reprinting. Stores the exact PDF bytes plus a structured JSON snapshot and a
    /// checksum for integrity.
    /// </summary>
    public sealed class ArchiveDocument : EntityBase
    {
        /// <summary>Foreign key to the archived payslip.</summary>
        public long PayslipId { get; set; }

        /// <summary>Language the document was generated in ("fr" or "ar").</summary>
        public string LanguageCode { get; set; }

        /// <summary>The exact rendered PDF bytes (the legal reprint).</summary>
        public byte[] PdfContent { get; set; }

        /// <summary>Structured snapshot of the payslip data (defensive redundancy).</summary>
        public string SnapshotJson { get; set; }

        /// <summary>Integrity checksum of the stored content.</summary>
        public string Checksum { get; set; }
    }
}
