namespace OptiPaie.Data.Migrations
{
    /// <summary>
    /// An ordered, embedded SQL migration script (e.g. "0001_InitialSchema").
    /// </summary>
    internal sealed class MigrationScript
    {
        /// <summary>Numeric version parsed from the file name prefix (e.g. 1).</summary>
        public int Version { get; }

        /// <summary>Descriptive script name without extension (e.g. "0001_InitialSchema").</summary>
        public string Name { get; }

        /// <summary>The SQL text of the script.</summary>
        public string Sql { get; }

        public MigrationScript(int version, string name, string sql)
        {
            Version = version;
            Name = name;
            Sql = sql;
        }
    }
}
