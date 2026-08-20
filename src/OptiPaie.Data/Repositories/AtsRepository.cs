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
                " ContractType, Deadline, ResponsibleName, ClosureType, ClosureReason, " +
                " CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@CompanyId, @Title, @Department, @Description, @Status, @OpenDate, @Positions, @Notes, " +
                " @ContractType, @Deadline, @ResponsibleName, @ClosureType, @ClosureReason, " +
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
                "ContractType = @ContractType, Deadline = @Deadline, ResponsibleName = @ResponsibleName, " +
                "ClosureType = @ClosureType, ClosureReason = @ClosureReason, " +
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

        public IEnumerable<Candidate> GetCandidatesByCompany(long companyId)
        {
            // Company scope via the posting — never returns another company's candidates.
            return Connection.Query<Candidate>(
                "SELECT c.* FROM Candidates c " +
                "JOIN JobPostings p ON p.Id = c.PostingId " +
                "WHERE p.CompanyId = @companyId AND c.IsDeleted = 0 AND p.IsDeleted = 0 " +
                "ORDER BY c.Stage, c.AppliedDate DESC, c.Id DESC;",
                new { companyId }, Transaction);
        }

        public long InsertCandidate(Candidate candidate)
        {
            candidate.CreatedAtUtc = DateTime.UtcNow;
            candidate.AppliedDate = SqliteDate.Day(candidate.AppliedDate);

            const string sql =
                "INSERT INTO Candidates " +
                "(PostingId, FirstName, LastName, Phone, Email, Stage, Rating, Source, Notes, AppliedDate, " +
                " EducationLevel, ExperienceYears, ClosureType, ClosureReason, ClosureDate, " +
                " HiredEmployeeId, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES " +
                "(@PostingId, @FirstName, @LastName, @Phone, @Email, @Stage, @Rating, @Source, @Notes, @AppliedDate, " +
                " @EducationLevel, @ExperienceYears, @ClosureType, @ClosureReason, @ClosureDate, " +
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
                "AppliedDate = @AppliedDate, EducationLevel = @EducationLevel, ExperienceYears = @ExperienceYears, " +
                "ClosureType = @ClosureType, ClosureReason = @ClosureReason, ClosureDate = @ClosureDate, " +
                "HiredEmployeeId = @HiredEmployeeId, UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";

            Connection.Execute(sql, candidate, Transaction);
        }

        public void SoftDeleteCandidate(long id)
        {
            Connection.Execute(
                "UPDATE Candidates SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);
        }

        // -- interviews --------------------------------------------------------

        public long InsertInterview(Interview interview)
        {
            interview.CreatedAtUtc = DateTime.UtcNow;
            interview.ScheduledDate = SqliteDate.Day(interview.ScheduledDate);

            const string sql =
                "INSERT INTO Interviews " +
                "(CandidateId, ScheduledDate, Type, Interviewer, Result, Notes, CreatedAtUtc, IsDeleted) " +
                "VALUES (@CandidateId, @ScheduledDate, @Type, @Interviewer, @Result, @Notes, @CreatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, interview, Transaction);
            interview.Id = id;
            return id;
        }

        public IEnumerable<Interview> GetInterviewsByCandidate(long candidateId)
        {
            return Connection.Query<Interview>(
                "SELECT * FROM Interviews WHERE CandidateId = @candidateId AND IsDeleted = 0 " +
                "ORDER BY ScheduledDate DESC, Id DESC;",
                new { candidateId }, Transaction);
        }

        public void SoftDeleteInterview(long id)
        {
            Connection.Execute(
                "UPDATE Interviews SET IsDeleted = 1 WHERE Id = @id;", new { id }, Transaction);
        }

        // -- attachments -------------------------------------------------------

        public long InsertAttachment(CandidateAttachment attachment)
        {
            attachment.CreatedAtUtc = DateTime.UtcNow;
            if (attachment.AddedAt == default(DateTime)) attachment.AddedAt = DateTime.UtcNow;

            const string sql =
                "INSERT INTO CandidateAttachments " +
                "(CandidateId, FileName, RelativePath, Kind, AddedAt, CreatedAtUtc, IsDeleted) " +
                "VALUES (@CandidateId, @FileName, @RelativePath, @Kind, @AddedAt, @CreatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";

            long id = Connection.ExecuteScalar<long>(sql, attachment, Transaction);
            attachment.Id = id;
            return id;
        }

        public IEnumerable<CandidateAttachment> GetAttachmentsByCandidate(long candidateId)
        {
            return Connection.Query<CandidateAttachment>(
                "SELECT * FROM CandidateAttachments WHERE CandidateId = @candidateId AND IsDeleted = 0 " +
                "ORDER BY AddedAt DESC, Id DESC;",
                new { candidateId }, Transaction);
        }

        public void SoftDeleteAttachment(long id)
        {
            Connection.Execute(
                "UPDATE CandidateAttachments SET IsDeleted = 1 WHERE Id = @id;", new { id }, Transaction);
        }
    }
}
