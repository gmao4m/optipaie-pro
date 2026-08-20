using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Recruitment module — the Phase-B guarantees: a strictly-ordered pipeline enforced in the
    /// SERVICE, a mandatory reason on closure, an ATOMIC hire (all-or-nothing, rollback proven),
    /// and strict per-company isolation.
    /// </summary>
    [TestFixture]
    public sealed class RecruitmentServiceTests
    {
        private string _dir;
        private IUnitOfWorkFactory _uowf;
        private AtsService _service;
        private long _companyA;
        private long _companyB;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-recruit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uowf = new UnitOfWorkFactory(factory);
            _service = new AtsService(_uowf, new EmployeeValidator());

            using (IUnitOfWork uow = _uowf.Create())
            {
                uow.BeginTransaction();
                _companyA = uow.Companies.Insert(new Company { NameFr = "SARL A", Nif = "000000000000001" });
                _companyB = uow.Companies.Insert(new Company { NameFr = "SARL B", Nif = "000000000000002" });
                uow.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // -- strict state machine ---------------------------------------------

        [Test]
        public void MoveStage_SkippingAStage_IsRejected()
        {
            long cand = NewCandidate(_companyA);
            // Applied(1) -> Offer(4) is a skip.
            Assert.That(_service.MoveStage(cand, CandidateStage.Offer).IsFailure, Is.True);
            Assert.That(_service.GetCandidate(cand).Stage, Is.EqualTo(CandidateStage.Applied));
        }

        [Test]
        public void MoveNext_WalksExactlyOneStep_ThenBackOneStep()
        {
            long cand = NewCandidate(_companyA);
            Assert.That(_service.MoveNext(cand).IsSuccess, Is.True);
            Assert.That(_service.GetCandidate(cand).Stage, Is.EqualTo(CandidateStage.Screening));
            _service.MoveNext(cand);
            Assert.That(_service.GetCandidate(cand).Stage, Is.EqualTo(CandidateStage.Interview));

            Assert.That(_service.MoveBack(cand).IsSuccess, Is.True);
            Assert.That(_service.GetCandidate(cand).Stage, Is.EqualTo(CandidateStage.Screening));
        }

        [Test]
        public void Hire_BeforeRetenu_IsRejected_NoEmployeeCreated()
        {
            long cand = NewCandidate(_companyA);           // at Applied
            Result<HireResult> r = _service.Hire(cand);
            Assert.That(r.IsFailure, Is.True);
            Assert.That(r.ErrorCode, Is.EqualTo("Ats_MustBeOffer"));
            Assert.That(EmployeeCount(_companyA), Is.EqualTo(0));
        }

        // -- closure requires a reason ----------------------------------------

        [Test]
        public void RejectAndDesist_RequireAReason()
        {
            long cand = NewCandidate(_companyA);
            Assert.That(_service.Reject(cand, "  ").ErrorCode, Is.EqualTo("Ats_ReasonRequired"));
            Assert.That(_service.Desist(cand, null).ErrorCode, Is.EqualTo("Ats_ReasonRequired"));
            Assert.That(_service.GetCandidate(cand).Stage, Is.EqualTo(CandidateStage.Applied), "still open");
        }

        [Test]
        public void Reject_And_Desist_CloseWithQualificationAndReason()
        {
            long r = NewCandidate(_companyA);
            Assert.That(_service.Reject(r, "Profil non retenu").IsSuccess, Is.True);
            Candidate rc = _service.GetCandidate(r);
            Assert.That(rc.Stage, Is.EqualTo(CandidateStage.Rejected));
            Assert.That(rc.ClosureType, Is.EqualTo(CandidateClosure.Rejected));
            Assert.That(rc.ClosureReason, Is.EqualTo("Profil non retenu"));
            Assert.That(rc.ClosureDate, Is.Not.Null);

            long d = NewCandidate(_companyA);
            Assert.That(_service.Desist(d, "A décliné l'offre").IsSuccess, Is.True);
            Candidate dc = _service.GetCandidate(d);
            Assert.That(dc.Stage, Is.EqualTo(CandidateStage.Rejected), "same underlying closed stage");
            Assert.That(dc.ClosureType, Is.EqualTo(CandidateClosure.Withdrawn), "but qualified as a withdrawal");
        }

        // -- hire atomicity (rollback proven) ---------------------------------

        [Test]
        public void Hire_IsAtomic_AnInjectedFailureLeavesNoPartialRow()
        {
            long postingId = _service.SavePosting(Posting(_companyA, 1)).Value;
            long cand = ReadyToHire(postingId);

            // A service whose posting-update throws mid-hire (after the employee + candidate +
            // contract writes) — the whole transaction must roll back.
            var failing = new AtsService(new FailingAtsFactory(_uowf), new EmployeeValidator());

            Assert.Throws<InvalidOperationException>(() => failing.Hire(cand));

            // Nothing partial survived.
            Assert.That(EmployeeCount(_companyA), Is.EqualTo(0), "no orphan employee");
            Candidate after = _service.GetCandidate(cand);
            Assert.That(after.Stage, Is.EqualTo(CandidateStage.Offer), "candidate not marked hired");
            Assert.That(after.HiredEmployeeId, Is.Null);
            Assert.That(_service.GetPosting(postingId).Status, Is.EqualTo(JobStatus.Open), "posting not filled");
        }

        [Test]
        public void Hire_Success_CreatesEmployee_DraftContract_AndFillsPosting()
        {
            long postingId = _service.SavePosting(Posting(_companyA, 1)).Value;
            long cand = ReadyToHire(postingId);

            Result<HireResult> hired = _service.Hire(cand);
            Assert.That(hired.IsSuccess, Is.True, hired.Error);
            Assert.That(hired.Value.EmployeeId, Is.GreaterThan(0));
            Assert.That(hired.Value.ContractId, Is.GreaterThan(0), "a draft contract is created");
            Assert.That(hired.Value.PostingFilled, Is.True);

            using (IUnitOfWork uow = _uowf.Create())
            {
                EmploymentContract contract = uow.Contracts.GetById(hired.Value.ContractId);
                Assert.That(contract, Is.Not.Null);
                Assert.That(contract.Status, Is.EqualTo(ContractStatus.Draft), "activation stays manual");
                Assert.That(contract.EmployeeId, Is.EqualTo(hired.Value.EmployeeId));
            }
        }

        // -- company isolation -------------------------------------------------

        [Test]
        public void Queries_AreStrictlyCompanyScoped()
        {
            long pa = _service.SavePosting(Posting(_companyA, 1)).Value;
            long pb = _service.SavePosting(Posting(_companyB, 1)).Value;
            _service.SaveCandidate(MakeCandidate(pa, "AlphaLast"));
            _service.SaveCandidate(MakeCandidate(pb, "BetaLast"));

            Assert.That(_service.GetPostingsByCompany(_companyA).Select(p => p.PostingId), Is.EquivalentTo(new[] { pa }));
            Assert.That(_service.GetCandidatesByCompany(_companyA).All(c => c.PostingId == pa), Is.True);
            Assert.That(_service.GetCandidatesByCompany(_companyB).All(c => c.PostingId == pb), Is.True);
        }

        [Test]
        public void CompanyScopedQueries_RejectAnInvalidCompany()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetPostingsByCompany(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetCandidatesByCompany(-1));
        }

        // -- helpers ----------------------------------------------------------

        private int EmployeeCount(long companyId)
        {
            using (IUnitOfWork uow = _uowf.Create()) return uow.Employees.GetByCompany(companyId).Count();
        }

        private JobPosting Posting(long companyId, int positions) => new JobPosting
        {
            CompanyId = companyId, Title = "Comptable", Department = "Finance",
            OpenDate = DateTime.Today, Positions = positions
        };

        private Candidate MakeCandidate(long postingId, string last) => new Candidate
        {
            PostingId = postingId, LastName = last, FirstName = "Karim", Phone = "0555000000",
            AppliedDate = DateTime.Today
        };

        private long NewCandidate(long companyId)
        {
            long postingId = _service.SavePosting(Posting(companyId, 1)).Value;
            return _service.SaveCandidate(MakeCandidate(postingId, "AMRANI")).Value;
        }

        private long ReadyToHire(long postingId)
        {
            long id = _service.SaveCandidate(MakeCandidate(postingId, "AMRANI")).Value;
            _service.MoveNext(id); _service.MoveNext(id); _service.MoveNext(id); // -> Offer
            return id;
        }

        // -- fault-injecting UoW (posting update throws mid-hire) -------------

        private sealed class FailingAtsFactory : IUnitOfWorkFactory
        {
            private readonly IUnitOfWorkFactory _inner;
            public FailingAtsFactory(IUnitOfWorkFactory inner) { _inner = inner; }
            public IUnitOfWork Create() => new FailingAtsUnitOfWork(_inner.Create());
        }

        private sealed class FailingAtsUnitOfWork : IUnitOfWork
        {
            private readonly IUnitOfWork _inner;
            private readonly IAtsRepository _ats;

            public FailingAtsUnitOfWork(IUnitOfWork inner)
            {
                _inner = inner;
                _ats = new ThrowingAtsRepository(inner.Ats);
            }

            public IAtsRepository Ats => _ats;

            public ICompanyRepository Companies => _inner.Companies;
            public IEmployeeRepository Employees => _inner.Employees;
            public IPayrollElementRepository PayrollElements => _inner.PayrollElements;
            public IEmployeeElementRepository EmployeeElements => _inner.EmployeeElements;
            public IPayrollRunRepository PayrollRuns => _inner.PayrollRuns;
            public IPayslipRepository Payslips => _inner.Payslips;
            public IPayrollDetailRepository PayrollDetails => _inner.PayrollDetails;
            public IArchiveDocumentRepository ArchiveDocuments => _inner.ArchiveDocuments;
            public ILegalParameterRepository LegalParameters => _inner.LegalParameters;
            public IAppSettingRepository AppSettings => _inner.AppSettings;
            public ILanguageRepository Languages => _inner.Languages;
            public IBackupRecordRepository BackupRecords => _inner.BackupRecords;
            public IAttendanceRepository Attendance => _inner.Attendance;
            public ILeaveRepository Leave => _inner.Leave;
            public ILeaveTypeRepository LeaveTypes => _inner.LeaveTypes;
            public IHolidayRepository Holidays => _inner.Holidays;
            public ILoanRepository Loans => _inner.Loans;
            public IContractRepository Contracts => _inner.Contracts;
            public IPerformanceRepository Performance => _inner.Performance;
            public IAssetRepository Assets => _inner.Assets;
            public IDepartmentRepository Departments => _inner.Departments;
            public ITrainingRepository Training => _inner.Training;
            public IWorkCertificateRepository Certificates => _inner.Certificates;
            public IAuditRepository Audit => _inner.Audit;
            public IUserRepository Users => _inner.Users;

            public void BeginTransaction() => _inner.BeginTransaction();
            public void Commit() => _inner.Commit();
            public void Rollback() => _inner.Rollback();
            public void Dispose() => _inner.Dispose();
        }

        private sealed class ThrowingAtsRepository : IAtsRepository
        {
            private readonly IAtsRepository _inner;
            public ThrowingAtsRepository(IAtsRepository inner) { _inner = inner; }

            public void UpdatePosting(JobPosting posting) =>
                throw new InvalidOperationException("Injected failure while filling the posting.");

            public JobPosting GetPostingById(long id) => _inner.GetPostingById(id);
            public IEnumerable<JobPosting> GetPostingsByCompany(long companyId) => _inner.GetPostingsByCompany(companyId);
            public long InsertPosting(JobPosting posting) => _inner.InsertPosting(posting);
            public void SoftDeletePosting(long id) => _inner.SoftDeletePosting(id);
            public Candidate GetCandidateById(long id) => _inner.GetCandidateById(id);
            public IEnumerable<Candidate> GetCandidatesByPosting(long postingId) => _inner.GetCandidatesByPosting(postingId);
            public IEnumerable<Candidate> GetCandidatesByCompany(long companyId) => _inner.GetCandidatesByCompany(companyId);
            public long InsertCandidate(Candidate candidate) => _inner.InsertCandidate(candidate);
            public void UpdateCandidate(Candidate candidate) => _inner.UpdateCandidate(candidate);
            public void SoftDeleteCandidate(long id) => _inner.SoftDeleteCandidate(id);
            public long InsertInterview(Interview interview) => _inner.InsertInterview(interview);
            public IEnumerable<Interview> GetInterviewsByCandidate(long candidateId) => _inner.GetInterviewsByCandidate(candidateId);
            public void SoftDeleteInterview(long id) => _inner.SoftDeleteInterview(id);
            public long InsertAttachment(CandidateAttachment attachment) => _inner.InsertAttachment(attachment);
            public IEnumerable<CandidateAttachment> GetAttachmentsByCandidate(long candidateId) => _inner.GetAttachmentsByCandidate(candidateId);
            public void SoftDeleteAttachment(long id) => _inner.SoftDeleteAttachment(id);
        }
    }
}
