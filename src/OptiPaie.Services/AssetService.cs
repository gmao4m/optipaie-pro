using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Asset orchestration. Owns the assign/return rules — one holder at a time, full
    /// history preserved — and resolves holders through the shared Employees table so no
    /// employee data is duplicated.
    /// </summary>
    public sealed class AssetService : IAssetService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public AssetService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        /// <summary>Optional audit sink (no-op unless wired by composition).</summary>
        public IAuditSink Audit { get; set; } = NullAuditSink.Instance;

        public Result<long> Save(Asset asset)
        {
            if (asset == null)
            {
                return Result.Fail<long>("Aucun matériel.", "Asset_Required");
            }

            if (string.IsNullOrWhiteSpace(asset.Name))
            {
                return Result.Fail<long>("Le nom du matériel est obligatoire.", "Asset_NameRequired");
            }

            if (asset.PurchaseValue < 0m)
            {
                return Result.Fail<long>("La valeur ne peut pas être négative.", "Asset_ValueInvalid");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (asset.CompanyId <= 0)
                {
                    return Result.Fail<long>("Entreprise obligatoire.", "Asset_CompanyRequired");
                }

                if (asset.Id > 0)
                {
                    Asset existing = uow.Assets.GetById(asset.Id);
                    if (existing == null)
                    {
                        return Result.Fail<long>("Matériel introuvable.", "Asset_NotFound");
                    }

                    // The status is driven by assign/return/SetStatus, not by an edit.
                    asset.Status = existing.Status;
                    asset.CreatedAtUtc = existing.CreatedAtUtc;
                    uow.Assets.Update(asset);
                    return Result.Ok(asset.Id);
                }

                asset.Status = AssetStatus.Available;
                return Result.Ok(uow.Assets.Insert(asset));
            }
        }

        public Result Assign(long assetId, long employeeId, DateTime date, string conditionOut, string notes)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Asset asset = uow.Assets.GetById(assetId);
                if (asset == null)
                {
                    return Result.Fail("Matériel introuvable.", "Asset_NotFound");
                }

                if (asset.Status == AssetStatus.Retired)
                {
                    return Result.Fail("Un matériel réformé ne peut pas être attribué.", "Asset_Retired");
                }

                if (!uow.Employees.ExistsById(employeeId))
                {
                    return Result.Fail("Employé introuvable.", "Asset_EmployeeNotFound");
                }

                var openAssignments = uow.Assets.GetAssignmentsByAsset(assetId)
                    .Where(a => a.ReturnedDate == null).ToList();

                if (asset.IsShared)
                {
                    // Shared: several holders at once, but never the same employee twice.
                    if (openAssignments.Any(a => a.EmployeeId == employeeId))
                    {
                        return Result.Fail("Cet employé détient déjà ce matériel partagé.", "Asset_AlreadyHeldByEmployee");
                    }
                }
                else
                {
                    // Exclusive: one holder at a time.
                    if (asset.Status == AssetStatus.Assigned || openAssignments.Count > 0)
                    {
                        return Result.Fail("Ce matériel est déjà attribué — enregistrez d'abord son retour.", "Asset_AlreadyAssigned");
                    }
                }

                uow.BeginTransaction();
                try
                {
                    uow.Assets.InsertAssignment(new AssetAssignment
                    {
                        AssetId = assetId,
                        EmployeeId = employeeId,
                        AssignedDate = date.Date,
                        ConditionOut = conditionOut,
                        Notes = notes
                    });

                    asset.Status = AssetStatus.Assigned;
                    uow.Assets.Update(asset);

                    uow.Commit();
                    Audit.Record("Asset", assetId, AuditAction.Assigned, "Matériel attribué", "Disponible", "Attribué");
                    return Result.Ok();
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public Result Return(long assetId, DateTime date, string conditionIn)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Asset asset = uow.Assets.GetById(assetId);
                if (asset == null)
                {
                    return Result.Fail("Matériel introuvable.", "Asset_NotFound");
                }

                var open = uow.Assets.GetAssignmentsByAsset(assetId).Where(a => a.ReturnedDate == null).ToList();
                if (open.Count == 0)
                {
                    return Result.Fail("Ce matériel n'est pas attribué.", "Asset_NotAssigned");
                }

                if (open.Count > 1)
                {
                    // A shared asset with several holders — the caller must say which one.
                    return Result.Fail("Ce matériel partagé a plusieurs détenteurs : indiquez lequel retourne.", "Asset_MultipleHolders");
                }

                return CloseAssignment(uow, asset, open[0], date, conditionIn);
            }
        }

        public Result ReturnFrom(long assetId, long employeeId, DateTime date, string conditionIn)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Asset asset = uow.Assets.GetById(assetId);
                if (asset == null)
                {
                    return Result.Fail("Matériel introuvable.", "Asset_NotFound");
                }

                AssetAssignment open = uow.Assets.GetAssignmentsByAsset(assetId)
                    .FirstOrDefault(a => a.ReturnedDate == null && a.EmployeeId == employeeId);
                if (open == null)
                {
                    return Result.Fail("Cet employé ne détient pas ce matériel.", "Asset_NotHeldByEmployee");
                }

                return CloseAssignment(uow, asset, open, date, conditionIn);
            }
        }

        /// <summary>
        /// Closes one assignment and only frees the asset (status → Disponible) once NO
        /// open assignment remains — so returning one holder of a shared asset never ends
        /// another holder's still-active assignment.
        /// </summary>
        private Result CloseAssignment(IUnitOfWork uow, Asset asset, AssetAssignment open, DateTime date, string conditionIn)
        {
            if (date.Date < open.AssignedDate.Date)
            {
                return Result.Fail("La date de retour ne peut pas précéder l'attribution.", "Asset_ReturnBeforeAssign");
            }

            uow.BeginTransaction();
            try
            {
                open.ReturnedDate = date.Date;
                open.ConditionIn = conditionIn;
                uow.Assets.UpdateAssignment(open);

                bool stillHeld = uow.Assets.GetAssignmentsByAsset(asset.Id).Any(a => a.ReturnedDate == null);
                if (!stillHeld)
                {
                    asset.Status = AssetStatus.Available;
                    uow.Assets.Update(asset);
                }

                uow.Commit();
                Audit.Record("Asset", asset.Id, AuditAction.Returned, "Matériel retourné",
                    "Attribué", stillHeld ? "Attribué (autres détenteurs)" : "Disponible");
                return Result.Ok();
            }
            catch
            {
                uow.Rollback();
                throw;
            }
        }

        public Result SetStatus(long assetId, AssetStatus status)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Asset asset = uow.Assets.GetById(assetId);
                if (asset == null)
                {
                    return Result.Fail("Matériel introuvable.", "Asset_NotFound");
                }

                if (uow.Assets.GetOpenAssignment(assetId) != null)
                {
                    return Result.Fail("Enregistrez d'abord le retour de ce matériel.", "Asset_StillAssigned");
                }

                asset.Status = status;
                uow.Assets.Update(asset);
                return Result.Ok();
            }
        }

        public Result Delete(long assetId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Asset asset = uow.Assets.GetById(assetId);
                if (asset == null)
                {
                    return Result.Ok();
                }

                if (uow.Assets.GetOpenAssignment(assetId) != null)
                {
                    return Result.Fail("Ce matériel est attribué — enregistrez son retour avant de le supprimer.", "Asset_StillAssigned");
                }

                uow.Assets.SoftDelete(assetId);
                return Result.Ok();
            }
        }

        public Asset Get(long assetId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Assets.GetById(assetId);
            }
        }

        public AssetSummary GetSummary(long assetId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Asset asset = uow.Assets.GetById(assetId);
                return asset == null ? null : Summarise(uow, asset);
            }
        }

        public IReadOnlyList<AssetSummary> GetByCompany(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Assets.GetByCompany(companyId).Select(a => Summarise(uow, a)).ToList();
            }
        }

        public IReadOnlyList<AssetAssignmentSummary> GetHistory(long assetId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Asset asset = uow.Assets.GetById(assetId);
                string assetName = asset != null ? asset.Name : null;

                return uow.Assets.GetAssignmentsByAsset(assetId)
                    .Select(a => ToSummary(uow, a, assetName)).ToList();
            }
        }

        public IReadOnlyList<AssetAssignmentSummary> GetHeldByEmployee(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Assets.GetOpenAssignmentsByEmployee(employeeId)
                    .Select(a => ToSummary(uow, a, uow.Assets.GetById(a.AssetId)?.Name)).ToList();
            }
        }

        // -- internals ---------------------------------------------------------

        private static AssetSummary Summarise(IUnitOfWork uow, Asset asset)
        {
            var summary = new AssetSummary
            {
                AssetId = asset.Id,
                CompanyId = asset.CompanyId,
                Name = asset.Name,
                Category = asset.Category,
                Status = asset.Status,
                SerialNumber = asset.SerialNumber,
                PurchaseValue = asset.PurchaseValue
            };

            AssetAssignment open = uow.Assets.GetOpenAssignment(asset.Id);
            if (open != null)
            {
                summary.HolderId = open.EmployeeId;
                summary.AssignedDate = open.AssignedDate;
                summary.HolderName = EmployeeName(uow, open.EmployeeId);
            }

            return summary;
        }

        private static AssetAssignmentSummary ToSummary(IUnitOfWork uow, AssetAssignment assignment, string assetName)
        {
            return new AssetAssignmentSummary
            {
                AssignmentId = assignment.Id,
                AssetId = assignment.AssetId,
                AssetName = assetName,
                EmployeeId = assignment.EmployeeId,
                EmployeeName = EmployeeName(uow, assignment.EmployeeId),
                AssignedDate = assignment.AssignedDate,
                ReturnedDate = assignment.ReturnedDate,
                ConditionOut = assignment.ConditionOut,
                ConditionIn = assignment.ConditionIn,
                IsOpen = !assignment.ReturnedDate.HasValue
            };
        }

        private static string EmployeeName(IUnitOfWork uow, long employeeId)
        {
            Employee employee = uow.Employees.GetById(employeeId);
            return employee == null ? null : (employee.LastNameFr + " " + employee.FirstNameFr).Trim();
        }
    }
}
