using System;

namespace OptiPaie.Core.Security
{
    /// <summary>
    /// Raised by <see cref="AccessPolicy.Require"/> when the signed-in role lacks the permission a
    /// service operation requires. This is the REAL refusal — hiding a button in the UI is only
    /// cosmetic; the operation is blocked here, at the service layer, even if reached directly.
    /// </summary>
    public sealed class AccessDeniedException : Exception
    {
        public const string UserMessage =
            "ليست لديك صلاحية القيام بهذه العملية.\n" +
            "Vous n'avez pas l'autorisation d'effectuer cette opération.";

        public AccessDeniedException(Permission permission)
            : base("Access denied: the current role is not permitted to " + permission + ".")
        {
            Permission = permission;
        }

        public Permission Permission { get; }
    }
}
