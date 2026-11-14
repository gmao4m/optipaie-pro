using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>CRUD for configurable leave types. Additive only; never touches the payroll engine.</summary>
    public sealed class LeaveTypeService : ILeaveTypeService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly OptiPaie.Core.Security.IAccessContext _access;

        public LeaveTypeService(IUnitOfWorkFactory unitOfWorkFactory, OptiPaie.Core.Security.IAccessContext access = null)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _access = access;
        }

        private void Require() =>
            OptiPaie.Core.Security.AccessPolicy.Require(_access, OptiPaie.Core.Security.Permission.ManageLeave);

        public IReadOnlyList<LeaveTypeDefinition> GetAll(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
                return uow.LeaveTypes.GetForCompany(companyId).ToList();
        }

        public LeaveTypeDefinition Get(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create()) return uow.LeaveTypes.GetById(id);
        }

        public Result<long> Save(LeaveTypeDefinition type)
        {
            Require();
            if (type == null) return Result.Fail<long>("Type manquant.", "LeaveType_Required");
            if (string.IsNullOrWhiteSpace(type.LabelAr)) return Result.Fail<long>("Le libellé arabe est obligatoire.", "LeaveType_LabelRequired");
            if ((int)type.BaseType < 1 || (int)type.BaseType > 5) return Result.Fail<long>("Type de base invalide.", "LeaveType_BaseInvalid");
            if (string.IsNullOrWhiteSpace(type.Code)) type.Code = "CUSTOM";

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (type.Id > 0)
                {
                    uow.LeaveTypes.Update(type);
                    return Result.Ok(type.Id);
                }

                return Result.Ok(uow.LeaveTypes.Insert(type));
            }
        }

        public Result SetActive(long id, bool active)
        {
            Require();
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                LeaveTypeDefinition t = uow.LeaveTypes.GetById(id);
                if (t == null) return Result.Fail("Type introuvable.", "LeaveType_NotFound");
                t.IsActive = active;
                uow.LeaveTypes.Update(t);
                return Result.Ok();
            }
        }
    }
}
