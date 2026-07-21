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
    /// Performance module — integration tests against a real SQLite file. They prove the
    /// weighted /20 scoring, the rating bands, the draft/complete lifecycle and the
    /// ecosystem pull: a review's attendance context comes live from the Attendance
    /// module, never duplicated.
    /// </summary>
    [TestFixture]
    public sealed class PerformanceServiceTests
    {
        private static readonly int Year = DateTime.Today.Year - 1;

        private string _directory;
        private IUnitOfWorkFactory _unitOfWorkFactory;
        private IPerformanceService _service;
        private IAttendanceService _attendance;

        private long _companyId;
        private long _employeeId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-perf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var connection = factory.CreateOpenConnection())
            {
                new MigrationRunner(connection).Run();
            }

            _unitOfWorkFactory = new UnitOfWorkFactory(factory);
            _attendance = new AttendanceService(_unitOfWorkFactory);
            _service = new PerformanceService(_unitOfWorkFactory, _attendance);

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
                _employeeId = uow.Employees.Insert(new Employee
                {
                    CompanyId = _companyId,
                    LastNameFr = "BENALI",
                    FirstNameFr = "Karim",
                    Gender = Gender.Male,
                    MaritalStatus = MaritalStatus.Single,
                    PaymentMode = PaymentMode.Cash,
                    ContractType = ContractType.Cdi,
                    HireDate = new DateTime(2020, 1, 1),
                    BaseSalary = 60000m,
                    IsActive = true
                });
                uow.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* the OS still holds the WAL file */ }
        }

        private List<PerformanceCriterion> Criteria(long reviewId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Performance.GetCriteria(reviewId).ToList();
            }
        }

        // ---------------------------------------------------------------- draft creation

        [Test]
        public void CreateDraft_SeedsTheDefaultCriteria()
        {
            Result<long> created = _service.CreateDraft(_employeeId, Year, Year.ToString(), "DRH");

            Assert.That(created.IsSuccess, Is.True, created.Error);
            List<PerformanceCriterion> criteria = Criteria(created.Value);
            Assert.That(criteria.Count, Is.EqualTo(5), "five default criteria are seeded");
            Assert.That(criteria.All(c => c.Weight == 1m), Is.True);
            Assert.That(_service.Get(created.Value).Status, Is.EqualTo(PerformanceStatus.Draft));
        }

        [Test]
        public void CreateDraft_UnknownEmployee_IsRejected()
        {
            Assert.That(_service.CreateDraft(999999, Year, "x", "DRH").IsFailure, Is.True);
        }

        // ---------------------------------------------------------------- scoring

        [Test]
        public void Save_ComputesTheWeightedAverageOnA20Scale()
        {
            long id = _service.CreateDraft(_employeeId, Year, Year.ToString(), "DRH").Value;

            var criteria = new List<PerformanceCriterion>
            {
                new PerformanceCriterion { Label = "Qualité", Weight = 2m, Score = 16m },
                new PerformanceCriterion { Label = "Productivité", Weight = 1m, Score = 10m }
            };

            Result saved = _service.Save(Header(id), criteria);

            Assert.That(saved.IsSuccess, Is.True, saved.Error);
            // (16*2 + 10*1) / 3 = 14.
            Assert.That(_service.Get(id).OverallScore, Is.EqualTo(14m));
        }

        [Test]
        public void Save_ScoreOutOfRange_IsRejected()
        {
            long id = _service.CreateDraft(_employeeId, Year, Year.ToString(), "DRH").Value;

            var criteria = new List<PerformanceCriterion>
            {
                new PerformanceCriterion { Label = "Qualité", Weight = 1m, Score = 25m }
            };

            Assert.That(_service.Save(Header(id), criteria).IsFailure, Is.True, "a /20 score cannot exceed 20");
        }

        [Test]
        public void Save_ReplacesTheCriteriaWholesale()
        {
            long id = _service.CreateDraft(_employeeId, Year, Year.ToString(), "DRH").Value;

            _service.Save(Header(id), new List<PerformanceCriterion>
            {
                new PerformanceCriterion { Label = "Unique", Weight = 1m, Score = 12m }
            });

            List<PerformanceCriterion> criteria = Criteria(id);
            Assert.That(criteria.Count, Is.EqualTo(1), "the five seeded criteria were replaced by the one provided");
            Assert.That(criteria[0].Label, Is.EqualTo("Unique"));
        }

        [Test]
        [TestCase(17, "Excellent")]
        [TestCase(15, "Très bien")]
        [TestCase(13, "Bien")]
        [TestCase(11, "Assez bien")]
        [TestCase(8, "Insuffisant")]
        public void Rate_MapsScoresToBands(int score, string expected)
        {
            Assert.That(_service.Rate(score), Is.EqualTo(expected));
        }

        // ---------------------------------------------------------------- lifecycle

        [Test]
        public void Complete_RequiresAReviewer()
        {
            long id = _service.CreateDraft(_employeeId, Year, Year.ToString(), null).Value;
            _service.Save(Header(id, reviewer: null), OneCriterion());

            Assert.That(_service.Complete(id).IsFailure, Is.True);
        }

        [Test]
        public void Complete_LocksTheReviewAgainstEditing()
        {
            long id = _service.CreateDraft(_employeeId, Year, Year.ToString(), "DRH").Value;
            _service.Save(Header(id), OneCriterion());

            Result completed = _service.Complete(id);
            Assert.That(completed.IsSuccess, Is.True, completed.Error);
            Assert.That(_service.Get(id).Status, Is.EqualTo(PerformanceStatus.Completed));

            Result saveAfter = _service.Save(Header(id), OneCriterion());
            Assert.That(saveAfter.IsFailure, Is.True, "a completed review must be reopened before editing");
        }

        [Test]
        public void Reopen_AllowsEditingAgain()
        {
            long id = _service.CreateDraft(_employeeId, Year, Year.ToString(), "DRH").Value;
            _service.Save(Header(id), OneCriterion());
            _service.Complete(id);

            _service.Reopen(id);

            Assert.That(_service.Get(id).Status, Is.EqualTo(PerformanceStatus.Draft));
            Assert.That(_service.Save(Header(id), OneCriterion()).IsSuccess, Is.True);
        }

        [Test]
        public void Delete_RemovesTheReview()
        {
            long id = _service.CreateDraft(_employeeId, Year, Year.ToString(), "DRH").Value;

            Assert.That(_service.Delete(id).IsSuccess, Is.True);
            Assert.That(_service.Get(id), Is.Null);
        }

        // ---------------------------------------------------------------- cross-module pull

        [Test]
        public void GetDetail_PullsTheAttendanceContextLiveFromTheAttendanceModule()
        {
            long id = _service.CreateDraft(_employeeId, Year, Year.ToString(), "DRH").Value;

            // Record attendance AFTER the review was created — the detail must reflect it.
            _attendance.Save(new AttendanceRecord
            {
                EmployeeId = _employeeId, WorkDate = new DateTime(Year, 3, 2),
                Status = AttendanceStatus.Present, CheckIn = "08:00", CheckOut = "18:00"
            });
            _attendance.Save(new AttendanceRecord
            {
                EmployeeId = _employeeId, WorkDate = new DateTime(Year, 3, 3),
                Status = AttendanceStatus.Absent
            });
            _attendance.Save(new AttendanceRecord
            {
                EmployeeId = _employeeId, WorkDate = new DateTime(Year, 3, 4),
                Status = AttendanceStatus.Present, CheckIn = "08:40", CheckOut = "16:00"
            });

            PerformanceDetail detail = _service.GetDetail(id);

            Assert.That(detail.Attendance, Is.Not.Null, "the review reflects the latest attendance");
            Assert.That(detail.Attendance.AbsentDays, Is.EqualTo(1));
            Assert.That(detail.Attendance.LateCount, Is.EqualTo(1));
            Assert.That(detail.Attendance.OvertimeHours, Is.EqualTo(2m), "10 h worked against the 8 h standard day");
        }

        [Test]
        public void GetDetail_NoAttendanceData_OmitsTheContext()
        {
            long id = _service.CreateDraft(_employeeId, Year, Year.ToString(), "DRH").Value;

            PerformanceDetail detail = _service.GetDetail(id);

            Assert.That(detail.Attendance, Is.Null, "with no pointage the section is simply omitted");
            Assert.That(detail.Criteria.Count, Is.EqualTo(5));
        }

        // ---------------------------------------------------------------- listing

        [Test]
        public void GetByCompanyYear_CarriesTheSharedNameAndRating()
        {
            long id = _service.CreateDraft(_employeeId, Year, Year.ToString(), "DRH").Value;
            _service.Save(Header(id), new List<PerformanceCriterion>
            {
                new PerformanceCriterion { Label = "Qualité", Weight = 1m, Score = 17m }
            });
            _service.Complete(id);

            IReadOnlyList<PerformanceSummary> reviews = _service.GetByCompanyYear(_companyId, Year);

            Assert.That(reviews.Count, Is.EqualTo(1));
            Assert.That(reviews[0].EmployeeName, Is.EqualTo("BENALI Karim"));
            Assert.That(reviews[0].Rating, Is.EqualTo("Excellent"));
            Assert.That(_service.GetByCompanyYear(_companyId, Year - 3).Count, Is.EqualTo(0));
        }

        private PerformanceReview Header(long id, string reviewer = "DRH")
        {
            return new PerformanceReview
            {
                Id = id,
                EmployeeId = _employeeId,
                PeriodYear = Year,
                PeriodLabel = Year.ToString(),
                ReviewDate = new DateTime(Year, 12, 20),
                Reviewer = reviewer,
                Comments = "RAS"
            };
        }

        private static List<PerformanceCriterion> OneCriterion()
        {
            return new List<PerformanceCriterion>
            {
                new PerformanceCriterion { Label = "Qualité", Weight = 1m, Score = 15m }
            };
        }
    }
}
