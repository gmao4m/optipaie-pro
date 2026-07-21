namespace OptiPaie.Core.Enums
{
    /// <summary>Kind of HR document issued to an employee.</summary>
    public enum CertificateType
    {
        /// <summary>Attestation de travail (currently employed).</summary>
        WorkCertificate = 1,

        /// <summary>Certificat de travail (end of employment).</summary>
        WorkExperience = 2,

        /// <summary>Attestation de salaire.</summary>
        SalaryCertificate = 3,

        /// <summary>Document libre (texte personnalisé).</summary>
        Custom = 99
    }
}
