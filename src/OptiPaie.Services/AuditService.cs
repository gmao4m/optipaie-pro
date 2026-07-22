using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Logging;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;

namespace OptiPaie.Services
{
    /// <summary>
    /// The audit trail. Modules record lifecycle changes through the <see cref="IAuditSink"/>
    /// write-side; history tabs and the activity feed read through the queries. Append-only;
    /// it never mutates business data or the payroll engine. Recording is best-effort — a
    /// logging failure never breaks the business operation that triggered it.
    /// </summary>
    public sealed class AuditService : IAuditService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly ILogger _logger;
        private readonly string _actor;

        public AuditService(IUnitOfWorkFactory unitOfWorkFactory, ILogger logger, string actor = null)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _logger = Guard.AgainstNull(logger, nameof(logger));
            _actor = string.IsNullOrWhiteSpace(actor) ? "Utilisateur" : actor;
        }

        public void Record(string entityType, long entityId, AuditAction action, string summary,
            string oldValue = null, string newValue = null)
        {
            try
            {
                using (IUnitOfWork uow = _unitOfWorkFactory.Create())
                {
                    uow.Audit.Insert(new AuditEntry
                    {
                        EntityType = entityType,
                        EntityId = entityId,
                        Action = action,
                        Summary = summary,
                        OldValue = oldValue,
                        NewValue = newValue,
                        Actor = _actor
                    });
                }
            }
            catch (Exception ex)
            {
                // The audit trail must never break the operation it is recording.
                _logger.Warn("Audit non enregistré (" + entityType + "#" + entityId + ") : " + ex.Message);
            }
        }

        public IReadOnlyList<AuditEntry> GetForEntity(string entityType, long entityId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Audit.GetForEntity(entityType, entityId).ToList();
            }
        }

        public IReadOnlyList<AuditEntry> GetRecent(int limit = 20)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Audit.GetRecent(limit).ToList();
            }
        }
    }
}
