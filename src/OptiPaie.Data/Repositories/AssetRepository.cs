using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>
    /// Dapper persistence for <see cref="Asset"/> and <see cref="AssetAssignment"/>.
    /// Assets are company-scoped; assignments reference the shared Employees table.
    /// </summary>
    internal sealed class AssetRepository : RepositoryBase, IAssetRepository
    {
        public AssetRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public Asset GetById(long id)
        {
            return Connection.QuerySingleOrDefault<Asset>(
                "SELECT * FROM Assets WHERE Id = @id AND IsDeleted = 0;", new { id }, Transaction);
        }

        public IEnumerable<Asset> GetByCompany(long companyId)
        {
            return Connection.Query<Asset>(
                "SELECT * FROM Assets WHERE CompanyId = @companyId AND IsDeleted = 0 " +
                "ORDER BY Status, Category, Name;",
                new { companyId }, Transaction);
        }

        public long Insert(Asset asset)
        {
            asset.CreatedAtUtc = DateTime.UtcNow;
            if (asset.PurchaseDate.HasValue) asset.PurchaseDate = SqliteDate.Day(asset.PurchaseDate.Value);

            const string sql =
                "INSERT INTO Assets " +
                "(CompanyId, Name, Category, Status, SerialNumber, PurchaseDate, PurchaseValue, Notes, " +
                " CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@CompanyId, @Name, @Category, @Status, @SerialNumber, @PurchaseDate, @PurchaseValue, @Notes, " +
                " @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, asset, Transaction);
            asset.Id = id;
            return id;
        }

        public void Update(Asset asset)
        {
            asset.UpdatedAtUtc = DateTime.UtcNow;
            if (asset.PurchaseDate.HasValue) asset.PurchaseDate = SqliteDate.Day(asset.PurchaseDate.Value);

            const string sql =
                "UPDATE Assets SET " +
                "CompanyId = @CompanyId, Name = @Name, Category = @Category, Status = @Status, " +
                "SerialNumber = @SerialNumber, PurchaseDate = @PurchaseDate, PurchaseValue = @PurchaseValue, " +
                "Notes = @Notes, UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, asset, Transaction);
        }

        public void SoftDelete(long id)
        {
            Connection.Execute(
                "UPDATE Assets SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }

        // -- assignments -------------------------------------------------------

        public AssetAssignment GetAssignmentById(long id)
        {
            return Connection.QuerySingleOrDefault<AssetAssignment>(
                "SELECT * FROM AssetAssignments WHERE Id = @id AND IsDeleted = 0;", new { id }, Transaction);
        }

        public AssetAssignment GetOpenAssignment(long assetId)
        {
            return Connection.QuerySingleOrDefault<AssetAssignment>(
                "SELECT * FROM AssetAssignments " +
                "WHERE AssetId = @assetId AND ReturnedDate IS NULL AND IsDeleted = 0 LIMIT 1;",
                new { assetId }, Transaction);
        }

        public IEnumerable<AssetAssignment> GetAssignmentsByAsset(long assetId)
        {
            return Connection.Query<AssetAssignment>(
                "SELECT * FROM AssetAssignments WHERE AssetId = @assetId AND IsDeleted = 0 " +
                "ORDER BY AssignedDate DESC, Id DESC;",
                new { assetId }, Transaction);
        }

        public IEnumerable<AssetAssignment> GetOpenAssignmentsByEmployee(long employeeId)
        {
            return Connection.Query<AssetAssignment>(
                "SELECT * FROM AssetAssignments " +
                "WHERE EmployeeId = @employeeId AND ReturnedDate IS NULL AND IsDeleted = 0 " +
                "ORDER BY AssignedDate DESC;",
                new { employeeId }, Transaction);
        }

        public long InsertAssignment(AssetAssignment assignment)
        {
            assignment.CreatedAtUtc = DateTime.UtcNow;
            assignment.AssignedDate = SqliteDate.Day(assignment.AssignedDate);
            if (assignment.ReturnedDate.HasValue) assignment.ReturnedDate = SqliteDate.Day(assignment.ReturnedDate.Value);

            const string sql =
                "INSERT INTO AssetAssignments " +
                "(AssetId, EmployeeId, AssignedDate, ReturnedDate, ConditionOut, ConditionIn, Notes, CreatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@AssetId, @EmployeeId, @AssignedDate, @ReturnedDate, @ConditionOut, @ConditionIn, @Notes, @CreatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, assignment, Transaction);
            assignment.Id = id;
            return id;
        }

        public void UpdateAssignment(AssetAssignment assignment)
        {
            assignment.AssignedDate = SqliteDate.Day(assignment.AssignedDate);
            if (assignment.ReturnedDate.HasValue) assignment.ReturnedDate = SqliteDate.Day(assignment.ReturnedDate.Value);

            const string sql =
                "UPDATE AssetAssignments SET " +
                "AssetId = @AssetId, EmployeeId = @EmployeeId, AssignedDate = @AssignedDate, " +
                "ReturnedDate = @ReturnedDate, ConditionOut = @ConditionOut, ConditionIn = @ConditionIn, " +
                "Notes = @Notes, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, assignment, Transaction);
        }
    }
}
