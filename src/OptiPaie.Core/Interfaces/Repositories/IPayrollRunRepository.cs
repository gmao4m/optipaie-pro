using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence operations for <see cref="PayrollRun"/>.</summary>
    public interface IPayrollRunRepository
    {
        /// <summary>Returns the run with the given id, or null.</summary>
        PayrollRun GetById(long id);

        /// <summary>Returns the run for a company and period, or null.</summary>
        PayrollRun GetByCompanyAndPeriod(long companyId, int year, int month);

        /// <summary>Searches runs by optional company, year and month, newest first.</summary>
        IEnumerable<PayrollRun> Search(long? companyId, int? year, int? month);

        /// <summary>Inserts a run and returns its new id.</summary>
        long Insert(PayrollRun run);

        /// <summary>Updates an existing run.</summary>
        void Update(PayrollRun run);

        /// <summary>True when a run already exists for the company and period.</summary>
        bool ExistsForPeriod(long companyId, int year, int month);
    }
}
