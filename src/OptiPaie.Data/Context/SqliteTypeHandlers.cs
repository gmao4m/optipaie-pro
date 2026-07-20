using System;
using System.Data;
using System.Globalization;
using Dapper;

namespace OptiPaie.Data.Context
{
    /// <summary>
    /// Registers the canonical Dapper type handling for the database layer.
    /// <para>
    /// Money and rates are stored as invariant-culture decimal strings so precision
    /// is exact and never affected by the active UI culture (Arabic/French use a
    /// comma as the decimal separator, which would otherwise corrupt parsing). This
    /// is the single, authoritative decimal persistence strategy.
    /// </para>
    /// </summary>
    public static class SqliteTypeHandlers
    {
        private static readonly object SyncRoot = new object();
        private static bool _registered;

        /// <summary>Registers the handlers once (idempotent and thread-safe).</summary>
        public static void Register()
        {
            if (_registered)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (_registered)
                {
                    return;
                }

                // Handles both decimal and decimal? (Dapper resolves the nullable underlying type).
                SqlMapper.AddTypeHandler(new InvariantDecimalHandler());
                _registered = true;
            }
        }

        /// <summary>
        /// Persists <see cref="decimal"/> as an invariant string and parses it back
        /// with invariant culture, regardless of the stored runtime type.
        /// </summary>
        private sealed class InvariantDecimalHandler : SqlMapper.TypeHandler<decimal>
        {
            public override void SetValue(IDbDataParameter parameter, decimal value)
            {
                parameter.DbType = DbType.String;
                parameter.Value = value.ToString(CultureInfo.InvariantCulture);
            }

            public override decimal Parse(object value)
            {
                if (value == null || value is DBNull)
                {
                    return 0m;
                }

                if (value is decimal decimalValue)
                {
                    return decimalValue;
                }

                if (value is double doubleValue)
                {
                    return (decimal)doubleValue;
                }

                if (value is long longValue)
                {
                    return longValue;
                }

                string text = Convert.ToString(value, CultureInfo.InvariantCulture);
                return decimal.Parse(text, NumberStyles.Any, CultureInfo.InvariantCulture);
            }
        }
    }
}
