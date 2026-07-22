using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>Dapper persistence for local user accounts.</summary>
    internal sealed class UserRepository : RepositoryBase, IUserRepository
    {
        public UserRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public User GetById(long id)
        {
            return Connection.QuerySingleOrDefault<User>(
                "SELECT * FROM Users WHERE Id = @id AND IsDeleted = 0;", new { id }, Transaction);
        }

        public User GetByUsername(string username)
        {
            return Connection.QuerySingleOrDefault<User>(
                "SELECT * FROM Users WHERE Username = @username AND IsDeleted = 0 AND IsActive = 1 LIMIT 1;",
                new { username }, Transaction);
        }

        public IEnumerable<User> GetAll()
        {
            return Connection.Query<User>(
                "SELECT * FROM Users WHERE IsDeleted = 0 ORDER BY Role, Username;", null, Transaction);
        }

        public long Insert(User user)
        {
            user.CreatedAtUtc = DateTime.UtcNow;
            const string sql =
                "INSERT INTO Users (Username, FullName, PasswordHash, Salt, Role, Department, IsActive, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES (@Username, @FullName, @PasswordHash, @Salt, @Role, @Department, @IsActive, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";
            long id = Connection.ExecuteScalar<long>(sql, user, Transaction);
            user.Id = id;
            return id;
        }

        public void Update(User user)
        {
            user.UpdatedAtUtc = DateTime.UtcNow;
            const string sql =
                "UPDATE Users SET Username = @Username, FullName = @FullName, PasswordHash = @PasswordHash, Salt = @Salt, " +
                "Role = @Role, Department = @Department, IsActive = @IsActive, UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";
            Connection.Execute(sql, user, Transaction);
        }

        public void SoftDelete(long id)
        {
            Connection.Execute(
                "UPDATE Users SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }

        public int CountActive()
        {
            return (int)Connection.ExecuteScalar<long>(
                "SELECT COUNT(1) FROM Users WHERE IsDeleted = 0 AND IsActive = 1;", null, Transaction);
        }

        public int CountAdmins()
        {
            return (int)Connection.ExecuteScalar<long>(
                "SELECT COUNT(1) FROM Users WHERE IsDeleted = 0 AND IsActive = 1 AND Role = 1;", null, Transaction);
        }
    }
}
