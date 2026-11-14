using System;

namespace OptiPaie.Core.Security
{
    /// <summary>
    /// Marks a <c>Result</c>-returning service method that the write-guard reflection test must NOT
    /// require to enforce a permission — because it does not actually change state, or its write is
    /// a consequence of an already-guarded operation. The <see cref="Reason"/> is mandatory and is
    /// printed by the test; an exemption without a reason fails the test.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class WriteGuardExemptAttribute : Attribute
    {
        public WriteGuardExemptAttribute(string reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }
}
