using System;
using System.Collections.Generic;
using Dapper;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;

namespace OptiPaie.Data.Repositories
{
    /// <summary>
    /// Dapper persistence for the Performance (Évaluation) module: templates + criteria,
    /// periods, evaluations + scored lines, and the behaviour log. Company-scoped queries
    /// join the shared Employees/Companies tables. Decimals are stored/read as invariant TEXT
    /// (see <see cref="SqliteTypeHandlers"/>); dates via <see cref="SqliteDate"/>.
    /// </summary>
    internal sealed class PerformanceRepository : RepositoryBase, IPerformanceRepository
    {
        public PerformanceRepository(UnitOfWork unitOfWork) : base(unitOfWork)
        {
        }

        private static DateTime? Day(DateTime? value) =>
            value.HasValue ? SqliteDate.Day(value.Value) : (DateTime?)null;

        // ===================== TEMPLATES =====================================

        public EvalTemplate GetTemplate(long id) =>
            Connection.QuerySingleOrDefault<EvalTemplate>(
                "SELECT * FROM EvalTemplates WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction);

        public IEnumerable<EvalTemplate> GetTemplatesForCompany(long companyId) =>
            Connection.Query<EvalTemplate>(
                "SELECT * FROM EvalTemplates " +
                "WHERE IsDeleted = 0 AND (CompanyId = @companyId OR CompanyId IS NULL) " +
                "ORDER BY IsBuiltIn, Department, Name;",
                new { companyId }, Transaction);

        public long InsertTemplate(EvalTemplate template)
        {
            template.CreatedAtUtc = DateTime.UtcNow;
            const string sql =
                "INSERT INTO EvalTemplates " +
                "(CompanyId, Department, Name, Description, WeightingMode, IsBuiltIn, IsDefault, " +
                " CreatedAtUtc, UpdatedAtUtc, IsDeleted) VALUES " +
                "(@CompanyId, @Department, @Name, @Description, @WeightingMode, @IsBuiltIn, @IsDefault, " +
                " @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); SELECT last_insert_rowid();";
            long id = Connection.ExecuteScalar<long>(sql, template, Transaction);
            template.Id = id;
            return id;
        }

        public void UpdateTemplate(EvalTemplate template)
        {
            template.UpdatedAtUtc = DateTime.UtcNow;
            const string sql =
                "UPDATE EvalTemplates SET " +
                "CompanyId = @CompanyId, Department = @Department, Name = @Name, Description = @Description, " +
                "WeightingMode = @WeightingMode, IsBuiltIn = @IsBuiltIn, IsDefault = @IsDefault, " +
                "UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted WHERE Id = @Id;";
            Connection.Execute(sql, template, Transaction);
        }

        public void SoftDeleteTemplate(long id) =>
            Connection.Execute(
                "UPDATE EvalTemplates SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);

        public void ClearDefaultTemplate(long companyId) =>
            Connection.Execute(
                "UPDATE EvalTemplates SET IsDefault = 0 WHERE CompanyId = @companyId AND IsDefault = 1;",
                new { companyId }, Transaction);

        // ===================== CRITERIA ======================================

        public IEnumerable<EvalCriterion> GetCriteria(long templateId) =>
            Connection.Query<EvalCriterion>(
                "SELECT * FROM EvalCriteria WHERE TemplateId = @templateId AND IsDeleted = 0 " +
                "ORDER BY SortOrder, Id;",
                new { templateId }, Transaction);

        public long InsertCriterion(EvalCriterion criterion)
        {
            criterion.CreatedAtUtc = DateTime.UtcNow;
            const string sql =
                "INSERT INTO EvalCriteria " +
                "(TemplateId, Name, Category, ScoreType, WeightPercent, KpiTarget, HigherIsBetter, " +
                " SortOrder, IsDeleted) VALUES " +
                "(@TemplateId, @Name, @Category, @ScoreType, @WeightPercent, @KpiTarget, @HigherIsBetter, " +
                " @SortOrder, @IsDeleted); SELECT last_insert_rowid();";
            long id = Connection.ExecuteScalar<long>(sql, criterion, Transaction);
            criterion.Id = id;
            return id;
        }

        public void DeleteCriteria(long templateId) =>
            Connection.Execute("DELETE FROM EvalCriteria WHERE TemplateId = @templateId;",
                new { templateId }, Transaction);

        // ===================== PERIODS =======================================

        public EvalPeriod GetPeriod(long id) =>
            Connection.QuerySingleOrDefault<EvalPeriod>(
                "SELECT * FROM EvalPeriods WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction);

        public IEnumerable<EvalPeriod> GetPeriodsByCompany(long companyId) =>
            Connection.Query<EvalPeriod>(
                "SELECT * FROM EvalPeriods WHERE CompanyId = @companyId AND IsDeleted = 0 " +
                "ORDER BY StartDate DESC, Id DESC;",
                new { companyId }, Transaction);

        public long InsertPeriod(EvalPeriod period)
        {
            period.CreatedAtUtc = DateTime.UtcNow;
            period.StartDate = SqliteDate.Day(period.StartDate);
            period.EndDate = SqliteDate.Day(period.EndDate);
            const string sql =
                "INSERT INTO EvalPeriods " +
                "(CompanyId, Name, Cadence, StartDate, EndDate, Status, CreatedAtUtc, UpdatedAtUtc, IsDeleted) " +
                "VALUES (@CompanyId, @Name, @Cadence, @StartDate, @EndDate, @Status, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";
            long id = Connection.ExecuteScalar<long>(sql, period, Transaction);
            period.Id = id;
            return id;
        }

        public void UpdatePeriod(EvalPeriod period)
        {
            period.UpdatedAtUtc = DateTime.UtcNow;
            period.StartDate = SqliteDate.Day(period.StartDate);
            period.EndDate = SqliteDate.Day(period.EndDate);
            const string sql =
                "UPDATE EvalPeriods SET " +
                "Name = @Name, Cadence = @Cadence, StartDate = @StartDate, EndDate = @EndDate, " +
                "Status = @Status, UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted WHERE Id = @Id;";
            Connection.Execute(sql, period, Transaction);
        }

        public void SoftDeletePeriod(long id) =>
            Connection.Execute(
                "UPDATE EvalPeriods SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);

        // ===================== EVALUATIONS ===================================

        public Evaluation GetEvaluation(long id) =>
            Connection.QuerySingleOrDefault<Evaluation>(
                "SELECT * FROM Evaluations WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction);

        public IEnumerable<Evaluation> GetEvaluationsByPeriod(long periodId) =>
            Connection.Query<Evaluation>(
                "SELECT * FROM Evaluations WHERE PeriodId = @periodId AND IsDeleted = 0;",
                new { periodId }, Transaction);

        public IEnumerable<Evaluation> GetEvaluationsByEmployee(long employeeId) =>
            Connection.Query<Evaluation>(
                "SELECT e.* FROM Evaluations e " +
                "INNER JOIN EvalPeriods p ON p.Id = e.PeriodId " +
                "WHERE e.EmployeeId = @employeeId AND e.IsDeleted = 0 AND p.IsDeleted = 0 " +
                "ORDER BY p.EndDate DESC, e.Id DESC;",
                new { employeeId }, Transaction);

        public IEnumerable<Evaluation> GetEvaluationsByCompany(long companyId) =>
            Connection.Query<Evaluation>(
                "SELECT e.* FROM Evaluations e " +
                "INNER JOIN EvalPeriods p ON p.Id = e.PeriodId " +
                "WHERE p.CompanyId = @companyId AND e.IsDeleted = 0 AND p.IsDeleted = 0 " +
                "ORDER BY p.EndDate DESC, e.Id DESC;",
                new { companyId }, Transaction);

        public Evaluation GetForEmployeeInPeriod(long periodId, long employeeId) =>
            Connection.QuerySingleOrDefault<Evaluation>(
                "SELECT * FROM Evaluations WHERE PeriodId = @periodId AND EmployeeId = @employeeId AND IsDeleted = 0;",
                new { periodId, employeeId }, Transaction);

        public long InsertEvaluation(Evaluation evaluation)
        {
            evaluation.CreatedAtUtc = DateTime.UtcNow;
            evaluation.EvaluatedDate = Day(evaluation.EvaluatedDate);
            const string sql =
                "INSERT INTO Evaluations " +
                "(PeriodId, EmployeeId, TemplateId, Department, WeightingMode, TotalScore, Status, " +
                " EvaluatedDate, Evaluator, Note, CreatedAtUtc, UpdatedAtUtc, IsDeleted) VALUES " +
                "(@PeriodId, @EmployeeId, @TemplateId, @Department, @WeightingMode, @TotalScore, @Status, " +
                " @EvaluatedDate, @Evaluator, @Note, @CreatedAtUtc, @UpdatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";
            long id = Connection.ExecuteScalar<long>(sql, evaluation, Transaction);
            evaluation.Id = id;
            return id;
        }

        public void UpdateEvaluation(Evaluation evaluation)
        {
            evaluation.UpdatedAtUtc = DateTime.UtcNow;
            evaluation.EvaluatedDate = Day(evaluation.EvaluatedDate);
            const string sql =
                "UPDATE Evaluations SET " +
                "TemplateId = @TemplateId, Department = @Department, WeightingMode = @WeightingMode, " +
                "TotalScore = @TotalScore, Status = @Status, EvaluatedDate = @EvaluatedDate, " +
                "Evaluator = @Evaluator, Note = @Note, UpdatedAtUtc = @UpdatedAtUtc, IsDeleted = @IsDeleted " +
                "WHERE Id = @Id;";
            Connection.Execute(sql, evaluation, Transaction);
        }

        public void SoftDeleteEvaluation(long id) =>
            Connection.Execute(
                "UPDATE Evaluations SET IsDeleted = 1, UpdatedAtUtc = @now WHERE Id = @id;",
                new { id, now = DateTime.UtcNow }, Transaction);

        // ===================== SCORED LINES ==================================

        public IEnumerable<EvaluationScore> GetScores(long evaluationId) =>
            Connection.Query<EvaluationScore>(
                "SELECT * FROM EvaluationScores WHERE EvaluationId = @evaluationId AND IsDeleted = 0 " +
                "ORDER BY SortOrder, Id;",
                new { evaluationId }, Transaction);

        public long InsertScore(EvaluationScore score)
        {
            score.CreatedAtUtc = DateTime.UtcNow;
            const string sql =
                "INSERT INTO EvaluationScores " +
                "(EvaluationId, CriterionName, Category, ScoreType, WeightPercent, RawValue, KpiTarget, " +
                " KpiActual, HigherIsBetter, NormalizedScore, Note, SortOrder, IsDeleted) VALUES " +
                "(@EvaluationId, @CriterionName, @Category, @ScoreType, @WeightPercent, @RawValue, @KpiTarget, " +
                " @KpiActual, @HigherIsBetter, @NormalizedScore, @Note, @SortOrder, @IsDeleted); " +
                "SELECT last_insert_rowid();";
            long id = Connection.ExecuteScalar<long>(sql, score, Transaction);
            score.Id = id;
            return id;
        }

        public void DeleteScores(long evaluationId) =>
            Connection.Execute("DELETE FROM EvaluationScores WHERE EvaluationId = @evaluationId;",
                new { evaluationId }, Transaction);

        // ===================== BEHAVIOUR LOG =================================

        public BehaviorLog GetBehavior(long id) =>
            Connection.QuerySingleOrDefault<BehaviorLog>(
                "SELECT * FROM BehaviorLogs WHERE Id = @id AND IsDeleted = 0;",
                new { id }, Transaction);

        public IEnumerable<BehaviorLog> GetBehaviorsByEmployee(long employeeId) =>
            Connection.Query<BehaviorLog>(
                "SELECT * FROM BehaviorLogs WHERE EmployeeId = @employeeId AND IsDeleted = 0 " +
                "ORDER BY OccurredAt DESC, Id DESC;",
                new { employeeId }, Transaction);

        public IEnumerable<BehaviorLog> GetBehaviorsByCompany(long companyId) =>
            Connection.Query<BehaviorLog>(
                "SELECT * FROM BehaviorLogs WHERE CompanyId = @companyId AND IsDeleted = 0 " +
                "ORDER BY OccurredAt DESC, Id DESC;",
                new { companyId }, Transaction);

        public IEnumerable<BehaviorLog> GetBehaviorsInRange(long employeeId, DateTime from, DateTime to) =>
            Connection.Query<BehaviorLog>(
                "SELECT * FROM BehaviorLogs WHERE EmployeeId = @employeeId AND IsDeleted = 0 " +
                "  AND OccurredAt >= @from AND OccurredAt <= @to " +
                "ORDER BY OccurredAt DESC, Id DESC;",
                new { employeeId, from = SqliteDate.Day(from), to = SqliteDate.Day(to) }, Transaction);

        public long InsertBehavior(BehaviorLog behavior)
        {
            behavior.CreatedAtUtc = DateTime.UtcNow;
            behavior.OccurredAt = SqliteDate.Day(behavior.OccurredAt);
            const string sql =
                "INSERT INTO BehaviorLogs " +
                "(CompanyId, EmployeeId, IsPositive, Note, OccurredAt, CreatedAtUtc, IsDeleted) VALUES " +
                "(@CompanyId, @EmployeeId, @IsPositive, @Note, @OccurredAt, @CreatedAtUtc, @IsDeleted); " +
                "SELECT last_insert_rowid();";
            long id = Connection.ExecuteScalar<long>(sql, behavior, Transaction);
            behavior.Id = id;
            return id;
        }

        public void SoftDeleteBehavior(long id) =>
            Connection.Execute(
                "UPDATE BehaviorLogs SET IsDeleted = 1 WHERE Id = @id;",
                new { id }, Transaction);
    }
}
