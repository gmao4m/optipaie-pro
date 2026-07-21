using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>
    /// Dapper persistence for <see cref="TrainingSession"/> and
    /// <see cref="TrainingParticipant"/>. Sessions are company-scoped; participants
    /// reference the shared Employees table.
    /// </summary>
    internal sealed class TrainingRepository : RepositoryBase, ITrainingRepository
    {
        public TrainingRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public TrainingSession GetById(long id)
        {
            return Connection.QuerySingleOrDefault<TrainingSession>(
                "SELECT * FROM TrainingSessions WHERE Id = @id AND IsDeleted = 0;", new { id }, Transaction);
        }

        public IEnumerable<TrainingSession> GetByCompany(long companyId)
        {
            return Connection.Query<TrainingSession>(
                "SELECT * FROM TrainingSessions WHERE CompanyId = @companyId AND IsDeleted = 0 " +
                "ORDER BY StartDate DESC, Id DESC;",
                new { companyId }, Transaction);
        }

        public long Insert(TrainingSession session)
        {
            session.CreatedAtUtc = DateTime.UtcNow;
            session.StartDate = SqliteDate.Day(session.StartDate);
            if (session.EndDate.HasValue) session.EndDate = SqliteDate.Day(session.EndDate.Value);

            const string sql =
                "INSERT INTO TrainingSessions " +
                "(CompanyId, Title, Category, Provider, Status, StartDate, EndDate, Location, Cost, Notes, " +
                " CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@CompanyId, @Title, @Category, @Provider, @Status, @StartDate, @EndDate, @Location, @Cost, @Notes, " +
                " @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, session, Transaction);
            session.Id = id;
            return id;
        }

        public void Update(TrainingSession session)
        {
            session.UpdatedAtUtc = DateTime.UtcNow;
            session.StartDate = SqliteDate.Day(session.StartDate);
            if (session.EndDate.HasValue) session.EndDate = SqliteDate.Day(session.EndDate.Value);

            const string sql =
                "UPDATE TrainingSessions SET " +
                "CompanyId = @CompanyId, Title = @Title, Category = @Category, Provider = @Provider, " +
                "Status = @Status, StartDate = @StartDate, EndDate = @EndDate, Location = @Location, " +
                "Cost = @Cost, Notes = @Notes, UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, session, Transaction);
        }

        public void SoftDelete(long id)
        {
            Connection.Execute(
                "UPDATE TrainingSessions SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }

        // -- participants ------------------------------------------------------

        public TrainingParticipant GetParticipantById(long id)
        {
            return Connection.QuerySingleOrDefault<TrainingParticipant>(
                "SELECT * FROM TrainingParticipants WHERE Id = @id AND IsDeleted = 0;", new { id }, Transaction);
        }

        public TrainingParticipant GetParticipant(long sessionId, long employeeId)
        {
            return Connection.QuerySingleOrDefault<TrainingParticipant>(
                "SELECT * FROM TrainingParticipants " +
                "WHERE SessionId = @sessionId AND EmployeeId = @employeeId AND IsDeleted = 0;",
                new { sessionId, employeeId }, Transaction);
        }

        public IEnumerable<TrainingParticipant> GetParticipants(long sessionId)
        {
            return Connection.Query<TrainingParticipant>(
                "SELECT * FROM TrainingParticipants WHERE SessionId = @sessionId AND IsDeleted = 0 ORDER BY Id;",
                new { sessionId }, Transaction);
        }

        public IEnumerable<TrainingParticipant> GetByEmployee(long employeeId)
        {
            return Connection.Query<TrainingParticipant>(
                "SELECT * FROM TrainingParticipants WHERE EmployeeId = @employeeId AND IsDeleted = 0 ORDER BY Id DESC;",
                new { employeeId }, Transaction);
        }

        public long InsertParticipant(TrainingParticipant participant)
        {
            participant.CreatedAtUtc = DateTime.UtcNow;

            const string sql =
                "INSERT INTO TrainingParticipants (SessionId, EmployeeId, Result, Score, CertificateRef, Notes, CreatedAtUtc, IsDeleted) " +
                "VALUES (@SessionId, @EmployeeId, @Result, @Score, @CertificateRef, @Notes, @CreatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, participant, Transaction);
            participant.Id = id;
            return id;
        }

        public void UpdateParticipant(TrainingParticipant participant)
        {
            const string sql =
                "UPDATE TrainingParticipants SET " +
                "SessionId = @SessionId, EmployeeId = @EmployeeId, Result = @Result, Score = @Score, " +
                "CertificateRef = @CertificateRef, Notes = @Notes, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, participant, Transaction);
        }

        public void DeleteParticipant(long id)
        {
            Connection.Execute("DELETE FROM TrainingParticipants WHERE Id = @id;", new { id }, Transaction);
        }
    }
}
