using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Department orchestration. Owns the per-company list, the lazy default seeding and
    /// the uniqueness rule. Deliberately thin: a department is just a name the employee
    /// dropdown and the evaluation criteria refer to.
    /// </summary>
    public sealed class DepartmentService : IDepartmentService
    {
        /// <summary>
        /// Default departments offered to a brand-new company. Chosen to line up with the
        /// evaluation module's pre-built criteria sets (Production, Commercial, ...).
        /// </summary>
        private static readonly string[] DefaultDepartments =
        {
            "Production",
            "Commercial",
            "Administration",
            "Informatique",
            "Générale"
        };

        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public DepartmentService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        public IReadOnlyList<Department> GetForCompany(long companyId)
        {
            if (companyId <= 0)
            {
                return Array.Empty<Department>();
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                List<Department> existing = uow.Departments.GetByCompany(companyId).ToList();
                if (existing.Count > 0)
                {
                    return existing;
                }

                SeedDefaults(uow, companyId);
                return uow.Departments.GetByCompany(companyId).ToList();
            }
        }

        public IReadOnlyList<string> GetNamesForCompany(long companyId)
        {
            return GetForCompany(companyId).Select(d => d.Name).ToList();
        }

        public Result<long> Save(Department department)
        {
            if (department == null)
            {
                return Result.Fail<long>("Aucun département.", "Department_Required");
            }

            if (string.IsNullOrWhiteSpace(department.Name))
            {
                return Result.Fail<long>("Le nom du département est obligatoire.", "Department_NameRequired");
            }

            if (department.CompanyId <= 0)
            {
                return Result.Fail<long>("Entreprise obligatoire.", "Department_CompanyRequired");
            }

            department.Name = department.Name.Trim();

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                bool duplicate = uow.Departments.GetByCompany(department.CompanyId)
                    .Any(d => d.Id != department.Id &&
                              string.Equals(d.Name, department.Name, StringComparison.OrdinalIgnoreCase));
                if (duplicate)
                {
                    return Result.Fail<long>("Ce département existe déjà.", "Department_Duplicate");
                }

                if (department.Id > 0)
                {
                    Department current = uow.Departments.GetById(department.Id);
                    if (current == null || current.IsDeleted)
                    {
                        return Result.Fail<long>("Département introuvable.", "Department_NotFound");
                    }

                    current.Name = department.Name;
                    current.DisplayOrder = department.DisplayOrder;
                    uow.Departments.Update(current);
                    return Result.Ok(current.Id);
                }

                if (department.DisplayOrder <= 0)
                {
                    List<Department> all = uow.Departments.GetByCompany(department.CompanyId).ToList();
                    department.DisplayOrder = all.Count == 0 ? 1 : all.Max(d => d.DisplayOrder) + 1;
                }

                long id = uow.Departments.Insert(department);
                return Result.Ok(id);
            }
        }

        public Result Remove(long departmentId)
        {
            if (departmentId <= 0)
            {
                return Result.Fail("Département introuvable.", "Department_NotFound");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Department current = uow.Departments.GetById(departmentId);
                if (current == null || current.IsDeleted)
                {
                    return Result.Fail("Département introuvable.", "Department_NotFound");
                }

                current.IsDeleted = true;
                uow.Departments.Update(current);
                return Result.Ok();
            }
        }

        /// <summary>
        /// Seeds the default set plus any distinct department names already recorded on the
        /// company's employees, so migrating an existing database surfaces real departments.
        /// </summary>
        private static void SeedDefaults(IUnitOfWork uow, long companyId)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string name in DefaultDepartments)
            {
                if (seen.Add(name))
                {
                    names.Add(name);
                }
            }

            foreach (Employee employee in uow.Employees.GetByCompany(companyId))
            {
                string name = employee.Department?.Trim();
                if (!string.IsNullOrEmpty(name) && seen.Add(name))
                {
                    names.Add(name);
                }
            }

            int order = 1;
            foreach (string name in names)
            {
                uow.Departments.Insert(new Department
                {
                    CompanyId = companyId,
                    Name = name,
                    DisplayOrder = order++
                });
            }
        }
    }
}
