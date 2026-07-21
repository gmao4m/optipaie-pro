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

namespace OptiPaie.Tests
{
    /// <summary>
    /// Recruitment/ATS module — integration tests against a real SQLite file. The core
    /// proof is the ecosystem link: hiring a candidate CREATES the shared employee (so the
    /// new hire is visible to every other module) and fills the posting when its positions
    /// are met.
    /// </summary>
    [TestFixture]
    public sealed class AtsServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _unitOfWorkFactory;
        private IAtsService _service;

        private long _companyId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-ats-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var connection = factory.CreateOpenConnection())
            {
                new MigrationRunner(connection).Run();
            }

            _unitOfWorkFactory = new UnitOfWorkFactory(factory);
            _service = new AtsService(_unitOfWorkFactory);

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
                uow.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* the OS still holds the WAL file */ }
        }

        private int EmployeeCount()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Employees.GetByCompany(_companyId).Count();
            }
        }

        // ---------------------------------------------------------------- postings

        [Test]
        public void SavePosting_NewPosting_IsOpen()
        {
            long id = _service.SavePosting(NewPosting("Comptable", 1)).Value;

            JobPostingSummary summary = _service.GetPostingSummary(id);
            Assert.That(summary.Status, Is.EqualTo(JobStatus.Open));
            Assert.That(summary.CandidateCount, Is.EqualTo(0));
        }

        [Test]
        public void SavePosting_WithoutTitle_IsRejected()
        {
            Assert.That(_service.SavePosting(NewPosting(null, 1)).IsFailure, Is.True);
        }

        // ---------------------------------------------------------------- candidates

        [Test]
        public void SaveCandidate_StartsAsApplied()
        {
            long postingId = _service.SavePosting(NewPosting("Comptable", 1)).Value;

            long candidateId = _service.SaveCandidate(NewCandidate(postingId, "AMRANI", "Yacine")).Value;

            Assert.That(_service.GetCandidate(candidateId).Stage, Is.EqualTo(CandidateStage.Applied));
        }

        [Test]
        public void MoveStage_AdvancesThePipeline()
        {
            long postingId = _service.SavePosting(NewPosting("Comptable", 1)).Value;
            long candidateId = _service.SaveCandidate(NewCandidate(postingId, "AMRANI", "Yacine")).Value;

            _service.MoveStage(candidateId, CandidateStage.Interview);

            Assert.That(_service.GetCandidate(candidateId).Stage, Is.EqualTo(CandidateStage.Interview));
        }

        [Test]
        public void MoveStage_ToHired_IsRejected_UseHireInstead()
        {
            long postingId = _service.SavePosting(NewPosting("Comptable", 1)).Value;
            long candidateId = _service.SaveCandidate(NewCandidate(postingId, "AMRANI", "Yacine")).Value;

            Assert.That(_service.MoveStage(candidateId, CandidateStage.Hired).IsFailure, Is.True);
        }

        // ---------------------------------------------------------------- hire (the link)

        [Test]
        public void Hire_CreatesTheSharedEmployee()
        {
            long postingId = _service.SavePosting(NewPosting("Comptable", 1)).Value;
            long candidateId = _service.SaveCandidate(NewCandidate(postingId, "AMRANI", "Yacine")).Value;

            Assert.That(EmployeeCount(), Is.EqualTo(0));

            Result<HireResult> hired = _service.Hire(candidateId);

            Assert.That(hired.IsSuccess, Is.True, hired.Error);
            Assert.That(EmployeeCount(), Is.EqualTo(1), "hiring creates the shared employee");

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Employee employee = uow.Employees.GetById(hired.Value.EmployeeId);
                Assert.That(employee, Is.Not.Null);
                Assert.That(employee.LastNameFr, Is.EqualTo("AMRANI"));
                Assert.That(employee.FirstNameFr, Is.EqualTo("Yacine"));
                Assert.That(employee.CompanyId, Is.EqualTo(_companyId), "in the posting's company");
                Assert.That(employee.Poste, Is.EqualTo("Comptable"));
            }

            Candidate candidate = _service.GetCandidate(candidateId);
            Assert.That(candidate.Stage, Is.EqualTo(CandidateStage.Hired));
            Assert.That(candidate.HiredEmployeeId, Is.EqualTo(hired.Value.EmployeeId));
        }

        [Test]
        public void Hire_FillsThePostingWhenPositionsAreMet()
        {
            long postingId = _service.SavePosting(NewPosting("Comptable", 1)).Value;
            long candidateId = _service.SaveCandidate(NewCandidate(postingId, "AMRANI", "Yacine")).Value;

            Result<HireResult> hired = _service.Hire(candidateId);

            Assert.That(hired.Value.PostingFilled, Is.True);
            Assert.That(_service.GetPosting(postingId).Status, Is.EqualTo(JobStatus.Filled));
        }

        [Test]
        public void Hire_DoesNotFillWhileMorePositionsRemain()
        {
            long postingId = _service.SavePosting(NewPosting("Comptable", 2)).Value; // two positions
            long c1 = _service.SaveCandidate(NewCandidate(postingId, "AMRANI", "Yacine")).Value;
            long c2 = _service.SaveCandidate(NewCandidate(postingId, "BOUDIAF", "Lina")).Value;

            _service.Hire(c1);
            Assert.That(_service.GetPosting(postingId).Status, Is.EqualTo(JobStatus.Open), "one of two filled");

            Result<HireResult> second = _service.Hire(c2);
            Assert.That(second.Value.PostingFilled, Is.True);
            Assert.That(EmployeeCount(), Is.EqualTo(2), "two shared employees created");
        }

        [Test]
        public void Hire_AnAlreadyHiredCandidate_IsRejected()
        {
            long postingId = _service.SavePosting(NewPosting("Comptable", 5)).Value;
            long candidateId = _service.SaveCandidate(NewCandidate(postingId, "AMRANI", "Yacine")).Value;
            _service.Hire(candidateId);

            Result<HireResult> again = _service.Hire(candidateId);

            Assert.That(again.IsFailure, Is.True, "no double hire — no duplicate employee");
            Assert.That(EmployeeCount(), Is.EqualTo(1));
        }

        [Test]
        public void DeleteCandidate_AfterHire_IsRejected_ToProtectTheRealEmployee()
        {
            long postingId = _service.SavePosting(NewPosting("Comptable", 5)).Value;
            long candidateId = _service.SaveCandidate(NewCandidate(postingId, "AMRANI", "Yacine")).Value;
            _service.Hire(candidateId);

            Assert.That(_service.DeleteCandidate(candidateId).IsFailure, Is.True);
            Assert.That(EmployeeCount(), Is.EqualTo(1), "the shared employee is never removed by the ATS");
        }

        // ---------------------------------------------------------------- listing

        [Test]
        public void GetPostingsByCompany_ReturnsCandidateAndHiredCounts()
        {
            long postingId = _service.SavePosting(NewPosting("Comptable", 3)).Value;
            long c1 = _service.SaveCandidate(NewCandidate(postingId, "AMRANI", "Yacine")).Value;
            _service.SaveCandidate(NewCandidate(postingId, "BOUDIAF", "Lina"));
            _service.Hire(c1);

            IReadOnlyList<JobPostingSummary> postings = _service.GetPostingsByCompany(_companyId);

            Assert.That(postings.Count, Is.EqualTo(1));
            Assert.That(postings[0].CandidateCount, Is.EqualTo(2));
            Assert.That(postings[0].HiredCount, Is.EqualTo(1));
        }

        [Test]
        public void Reject_MovesTheCandidateOutOfThePipeline()
        {
            long postingId = _service.SavePosting(NewPosting("Comptable", 1)).Value;
            long candidateId = _service.SaveCandidate(NewCandidate(postingId, "AMRANI", "Yacine")).Value;

            _service.Reject(candidateId);

            Assert.That(_service.GetCandidate(candidateId).Stage, Is.EqualTo(CandidateStage.Rejected));
        }

        private JobPosting NewPosting(string title, int positions)
        {
            return new JobPosting
            {
                CompanyId = _companyId,
                Title = title,
                Department = "Finance",
                OpenDate = DateTime.Today,
                Positions = positions
            };
        }

        private static Candidate NewCandidate(long postingId, string last, string first)
        {
            return new Candidate
            {
                PostingId = postingId,
                LastName = last,
                FirstName = first,
                Phone = "0555000000",
                Email = "x@example.dz",
                AppliedDate = DateTime.Today
            };
        }
    }
}
