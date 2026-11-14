using System;

namespace OptiPaie.Core.Primitives
{
    /// <summary>
    /// Raised by a repository when a versioned update matches no row — i.e. another user saved a
    /// change to the same record first (or removed it) since this editor loaded it. The service
    /// layer turns this into a friendly <see cref="Result"/> failure instead of silently
    /// overwriting the other user's work.
    /// <para>
    /// What the customer gets: two people can open the same employee / contract / leave / loan /
    /// attendance record, and the one who saves second is told to reload rather than wiping out the
    /// first save without anyone noticing.
    /// </para>
    /// </summary>
    public sealed class ConcurrencyException : Exception
    {
        /// <summary>
        /// The non-technical, bilingual (AR + FR) message shown to the user. It explicitly tells
        /// them to reopen the record — their own unsaved typing is never thrown away by the app.
        /// </summary>
        public const string UserMessage =
            "عدّل هذا السجلّ مستخدم آخر. أعد فتحه لترى آخر نسخة.\n" +
            "Un autre utilisateur a modifié cette fiche. Rouvrez-la pour voir la dernière version.";

        public ConcurrencyException(string entity)
            : base("Concurrency conflict on " + entity + ": the record was modified or removed by another user.")
        {
            Entity = entity;
        }

        /// <summary>The entity kind that conflicted (e.g. "Employee"), for logging.</summary>
        public string Entity { get; }
    }
}
