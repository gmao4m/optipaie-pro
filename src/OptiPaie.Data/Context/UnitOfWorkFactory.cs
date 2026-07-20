using System;
using OptiPaie.Core.Interfaces.Repositories;

namespace OptiPaie.Data.Context
{
    /// <summary>
    /// Creates a new <see cref="UnitOfWork"/> with a freshly opened connection per
    /// call. Injected into services so each operation gets a short-lived unit of
    /// work; trivially mockable in tests.
    /// </summary>
    public sealed class UnitOfWorkFactory : IUnitOfWorkFactory
    {
        private readonly SqliteConnectionFactory _connectionFactory;

        public UnitOfWorkFactory(SqliteConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public IUnitOfWork Create()
        {
            return new UnitOfWork(_connectionFactory.CreateOpenConnection());
        }
    }
}
