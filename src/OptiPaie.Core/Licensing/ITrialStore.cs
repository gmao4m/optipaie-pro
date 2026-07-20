namespace OptiPaie.Core.Licensing
{
    /// <summary>
    /// Persists the (encrypted) trial state blob in the shared SQLite database.
    /// Opaque to the store — the trial service encrypts/decrypts the content.
    /// </summary>
    public interface ITrialStore
    {
        /// <summary>The stored blob, or null when the trial has never been started.</summary>
        string Load();

        /// <summary>Saves the trial blob (single row).</summary>
        void Save(string blob);

        /// <summary>Removes the trial state.</summary>
        void Clear();
    }
}
