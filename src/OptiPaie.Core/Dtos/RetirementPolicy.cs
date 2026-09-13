using System;
using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Dtos
{
    /// <summary>
    /// Legal retirement ages, kept OUT of the computation logic so they can be changed by
    /// configuration without a code change. Algerian law (loi 83-12 modifiée) sets the base
    /// retirement age at 60 for men and lets women leave at 55; both values are configurable here.
    /// <para>NOT modelled (individual, not derivable from the shared record): the −1 year/child
    /// reduction for women (max 3) and the reductions for high-nuisance posts.</para>
    /// </summary>
    public sealed class RetirementPolicy
    {
        public RetirementPolicy(int maleAge, int femaleAge)
        {
            MaleAge = maleAge;
            FemaleAge = femaleAge;
        }

        public int MaleAge { get; }
        public int FemaleAge { get; }

        /// <summary>The legal ages currently in force in Algeria, used when nothing overrides them.</summary>
        public static RetirementPolicy Default => new RetirementPolicy(60, 55);

        /// <summary>The retirement age that applies to an employee of the given gender.</summary>
        public int AgeFor(Gender gender) => gender == Gender.Female ? FemaleAge : MaleAge;
    }

    /// <summary>
    /// One employee at or approaching legal retirement age. Language-neutral: it carries the id and
    /// the computed retirement date (birth date + the age for that gender); the UI loads the name and
    /// formats the dates. <see cref="AlreadyReached"/> distinguishes "already past the age" from
    /// "reaches it within the horizon".
    /// </summary>
    public sealed class RetirementCandidate
    {
        public long EmployeeId { get; set; }
        public DateTime RetirementDate { get; set; }
        public bool AlreadyReached { get; set; }
    }
}
