using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Constants;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Services.Validation;

namespace OptiPaie.Services
{
    /// <summary>Manages the payroll element catalog.</summary>
    public sealed class PayrollElementService : IPayrollElementService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly IValidator<PayrollElement> _validator;
        private readonly OptiPaie.Core.Security.IAccessContext _access;

        public PayrollElementService(IUnitOfWorkFactory unitOfWorkFactory, IValidator<PayrollElement> validator, OptiPaie.Core.Security.IAccessContext access = null)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _validator = Guard.AgainstNull(validator, nameof(validator));
            _access = access;
        }

        private void Require() =>
            OptiPaie.Core.Security.AccessPolicy.Require(_access, OptiPaie.Core.Security.Permission.ManagePayrollElements);

        public Result<long> Create(PayrollElement element)
        {
            Require();
            ValidationResult validation = _validator.Validate(element);
            if (!validation.IsValid)
            {
                return validation.ToFailure<long>();
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                long id = uow.PayrollElements.Insert(element);
                return Result.Ok(id);
            }
        }

        public Result Update(PayrollElement element)
        {
            Require();
            ValidationResult validation = _validator.Validate(element);
            if (!validation.IsValid)
            {
                return validation.ToFailure();
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (!uow.PayrollElements.ExistsById(element.Id))
                {
                    return Result.Fail("Rubrique introuvable.", ErrorCodes.NotFound);
                }

                try
                {
                    uow.PayrollElements.Update(element);
                }
                catch (ConcurrencyException)
                {
                    return Result.Fail(ConcurrencyException.UserMessage, ErrorCodes.Concurrency);
                }
                return Result.Ok();
            }
        }

        public Result Delete(long id)
        {
            Require();
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                PayrollElement element = uow.PayrollElements.GetById(id);
                if (element == null)
                {
                    return Result.Fail("Rubrique introuvable.", ErrorCodes.NotFound);
                }

                if (element.IsSystem)
                {
                    return Result.Fail("Les rubriques système ne peuvent pas être supprimées.", ErrorCodes.ElementSystemCannotDelete);
                }

                uow.PayrollElements.SoftDelete(id);
                return Result.Ok();
            }
        }

        public PayrollElement Get(long id)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.PayrollElements.GetById(id);
            }
        }

        public IReadOnlyList<PayrollElement> GetAll(bool includeDisabled = true)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.PayrollElements.GetAll(includeDisabled).ToList();
            }
        }
    }
}
