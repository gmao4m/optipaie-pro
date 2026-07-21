using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>
    /// Dapper persistence for <see cref="JobPosting"/> and <see cref="Candidate"/>.
    /// Postings are company-scoped; candidates belong to a posting.
    /// </summary>
    internal sealed class AtsRepository : RepositoryBase, IAtsRepository
    {
        public AtsRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        public JobPosting GetPostingById(long id)
        {
            return Connection.QuerySingleOrDefault<JobPosting>(
                "SELECT * FROM JobPostings WHERE Id = @id AND IsDeleted = 0;", new { id }, Transaction);
        }

        public IEnumerable<JobPosting> GetPostingsByCompany(long companyId)
        {
            return Connection.Query<JobPosting>(
                "SELECT * FROM JobPostings WHERE CompanyId = @companyId AND IsDeleted = 0 " +
                "ORDER BY Status, OpenDate DESC, Id DESC;",
                new { companyId }, Transaction);
        }

        public long InsertPosting(JobPosting posting)
        {
            posting.CreatedAtUtc = DateTime.UtcNow;
            posting.OpenDate = SqliteDate.Day(posting.OpenDate);

            const string sql =
                "INSERT INTO JobPostings " +
                "(CompanyId, Title, Department, Description, Status, OpenDate, Positions, Notes, " +
                " CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@CompanyId, @Title, @Department, @Description, @Status, @OpenDate, @Positions, @Notes, " +
                " @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, posting, Transaction);
            posting.Id = id;
            return id;
        }

        public void UpdatePosting(JobPosting posting)
        {
            posting.UpdatedAtUtc = DateTime.UtcNow;
            posting.OpenDate = SqliteDate.Day(posting.OpenDate);

            const string sql =
                "UPDATE JobPostings SET " +
                "CompanyId = @CompanyId, Title = @Title, Department = @Department, Description = @Description, " +
                "Status = @Status, OpenDate = @OpenDate, Positions = @Positions, Notes = @Notes, " +
                "UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, posting, Transaction);
        }

        public void SoftDeletePosting(long id)
        {
            Connection.Execute(
                "UPDATE JobPostings SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }

        // -- candidates --------------------------------------------------------

        public Candidate GetCandidateById(long id)
        {
            return Connection.QuerySingleOrDefault<Candidate>(
                "SELECT * FROM Candidates WHERE Id = @id AND IsDeleted = 0;", new { id }, Transaction);
        }

        public IEnumerable<Candidate> GetCandidatesByPosting(long postingId)
        {
            return Connection.Query<Candidate>(
                "SELECT * FROM Candidates WHERE PostingId = @postingId AND IsDeleted = 0 " +
                "ORDER BY Stage, AppliedDate DESC, Id DESC;",
                new { postingId }, Transaction);
        }

        public long InsertCandidate(Candidate candidate)
        {
            candidate.CreatedAtUtc = DateTime.UtcNow;
            candidate.AppliedDate = SqliteDate.Day(candidate.AppliedDate);

            const string sql =
                "INSERT INTO Candidates " +
                "(PostingId, FirstName, LastName, Phone, Email, Stage, Rating, Source, Notes, AppliedDate, " +
                " HiredEmployeeId, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@PostingId, @FirstName, @LastName, @Phone, @Email, @Stage, @Rating, @Source, @Notes, @AppliedDate, " +
                " @HiredEmployeeId, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, candidate, Transaction);
            candidate.Id = id;
            return id;
        }

        public void UpdateCandidate(Candidate candidate)
        {
            candidate.UpdatedAtUtc = DateTime.UtcNow;
            candidate.AppliedDate = SqliteDate.Day(candidate.AppliedDate);

            const string sql =
                "UPDATE Candidates SET " +
                "PostingId = @PostingId, FirstName = @FirstName, LastName = @LastName, Phone = @Phone, " +
                "Email = @Email, Stage = @Stage, Rating = @Rating, Source = @Source, Notes = @Notes, " +
                "AppliedDate = @AppliedDate, HiredEmployeeId = @HiredEmployeeId, UpdatedAtUtc = @UpdatedAtUtc, " +
                "IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, candidate, Transaction);
        }

        public void SoftDeleteCandidate(long id)
        {
            Connection.Execute(
                "UPDATE Candidates SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }
    }
}
