namespace OptiPaie.Core.Enums
{
    /// <summary>The five fairness bands a normalised 0-100 evaluation score falls into,
    /// from weakest to strongest.</summary>
    public enum ClassificationBand
    {
        /// <summary>ضعيف — Faible.</summary>
        Weak = 0,

        /// <summary>متوسط — Moyen.</summary>
        Average = 1,

        /// <summary>جيد — Bien.</summary>
        Good = 2,

        /// <summary>جيد جدًا — Très bien.</summary>
        VeryGood = 3,

        /// <summary>ممتاز — Excellent.</summary>
        Excellent = 4
    }
}
