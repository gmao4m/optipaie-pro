using System.Data;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>
    /// Base for Dapper repositories. Exposes the active connection and transaction
    /// owned by the <see cref="UnitOfWork"/>, read at call time so repositories
    /// always participate in the unit of work's current transaction (if any).
    /// </summary>
    internal abstract class RepositoryBase
    {
        private readonly UnitOfWork _unitOfWork;

        protected RepositoryBase(UnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        /// <summary>The shared open connection.</summary>
        protected IDbConnection Connection => _unitOfWork.Connection;

        /// <summary>The active transaction, or null when none is in progress.</summary>
        protected IDbTransaction Transaction => _unitOfWork.Transaction;
    }
}
