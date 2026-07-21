namespace OptiPaie.Core.Enums
{
    /// <summary>Lifecycle of a job posting.</summary>
    public enum JobStatus
    {
        /// <summary>Open — accepting candidates.</summary>
        Open = 1,

        /// <summary>Closed — no longer accepting candidates.</summary>
        Closed = 2,

        /// <summary>Filled — the position(s) have been staffed.</summary>
        Filled = 3
    }

    /// <summary>Stage of a candidate in the recruitment pipeline.</summary>
    public enum CandidateStage
    {
        /// <summary>Candidature reçue.</summary>
        Applied = 1,

        /// <summary>Présélection (tri des CV).</summary>
        Screening = 2,

        /// <summary>Entretien.</summary>
        Interview = 3,

        /// <summary>Offre / proposition.</summary>
        Offer = 4,

        /// <summary>Recruté — un employé partagé a été créé.</summary>
        Hired = 5,

        /// <summary>Écarté.</summary>
        Rejected = 6
    }
}
