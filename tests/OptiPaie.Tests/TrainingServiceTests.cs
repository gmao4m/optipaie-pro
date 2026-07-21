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
    /// Training module — integration tests against a real SQLite file. They prove the
    /// session lifecycle, the one-enrolment-per-employee rule, outcome recording, and
    /// that participants and history resolve through the shared Employees table.
    /// </summary>
    [TestFixture]
    public sealed class TrainingServiceTests
    {
        private string _directory;
        private IUnitOfWorkFactory _unitOfWorkFactory;
        private ITrainingService _service;

        private long _companyId;
        private long _employeeId;
        private long _otherEmployeeId;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-training-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var connection = factory.CreateOpenConnection())
            {
                new MigrationRunner(connection).Run();
            }

            _unitOfWorkFactory = new UnitOfWorkFactory(factory);
            _service = new TrainingService(_unitOfWorkFactory);

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.BeginTransaction();
                _companyId = uow.Companies.Insert(new Company { NameFr = "SARL Test", Nif = "000000000000000" });
                _employeeId = uow.Employees.Insert(NewEmployee("BENALI", "Karim"));
                _otherEmployeeId = uow.Employees.Insert(NewEmployee("HADDAD", "Sofiane"));
                uow.Commit();
            }
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { /* the OS still holds the WAL file */ }
        }

        private Employee NewEmployee(string last, string first)
        {
            return new Employee
            {
                CompanyId = _companyId,
                LastNameFr = last,
                FirstNameFr = first,
                Gender = Gender.Male,
                MaritalStatus = MaritalStatus.Single,
                PaymentMode = PaymentMode.Cash,
                ContractType = ContractType.Cdi,
                HireDate = new DateTime(2020, 1, 1),
                BaseSalary = 50000m,
                IsActive = true
            };
        }

        // ---------------------------------------------------------------- sessions

        [Test]
        public void Save_NewSession_IsPlanned()
        {
            long id = _service.Save(NewSession("Sécurité au travail", 80000m)).Value;

            TrainingSummary summary = _service.GetSummary(id);
            Assert.That(summary.Status, Is.EqualTo(TrainingStatus.Planned));
            Assert.That(summary.ParticipantCount, Is.EqualTo(0));
        }

        [Test]
        public void Save_WithoutTitle_IsRejected()
        {
            Assert.That(_service.Save(NewSession(null, 0m)).IsFailure, Is.True);
        }

        [Test]
        public void Save_EndBeforeStart_IsRejected()
        {
            TrainingSession session = NewSession("Bureautique", 10000m);
            session.StartDate = new DateTime(2026, 5, 10);
            session.EndDate = new DateTime(2026, 5, 1);

            Assert.That(_service.Save(session).IsFailure, Is.True);
        }

        [Test]
        public void SetStatus_MovesTheSessionThroughItsLifecycle()
        {
            long id = _service.Save(NewSession("Sécurité", 0m)).Value;

            _service.SetStatus(id, TrainingStatus.Ongoing);
            Assert.That(_service.Get(id).Status, Is.EqualTo(TrainingStatus.Ongoing));

            _service.SetStatus(id, TrainingStatus.Completed);
            Assert.That(_service.Get(id).Status, Is.EqualTo(TrainingStatus.Completed));
        }

        // ---------------------------------------------------------------- enrolment

        [Test]
        public void Enroll_AddsTheSharedEmployeeAsAParticipant()
        {
            long id = _service.Save(NewSession("Sécurité", 0m)).Value;

            Result enrolled = _service.Enroll(id, _employeeId);

            Assert.That(enrolled.IsSuccess, Is.True, enrolled.Error);
            IReadOnlyList<TrainingParticipantSummary> participants = _service.GetParticipants(id);
            Assert.That(participants.Count, Is.EqualTo(1));
            Assert.That(participants[0].EmployeeName, Is.EqualTo("BENALI Karim"), "name from the shared record");
            Assert.That(participants[0].Result, Is.EqualTo(TrainingResult.Enrolled));
        }

        [Test]
        public void Enroll_SameEmployeeTwice_IsRejected()
        {
            long id = _service.Save(NewSession("Sécurité", 0m)).Value;
            _service.Enroll(id, _employeeId);

            Assert.That(_service.Enroll(id, _employeeId).IsFailure, Is.True, "one enrolment per employee and session");
        }

        [Test]
        public void Enroll_UnknownEmployee_IsRejected()
        {
            long id = _service.Save(NewSession("Sécurité", 0m)).Value;

            Assert.That(_service.Enroll(id, 999999).IsFailure, Is.True);
        }

        [Test]
        public void Enroll_IntoACancelledSession_IsRejected()
        {
            long id = _service.Save(NewSession("Sécurité", 0m)).Value;
            _service.SetStatus(id, TrainingStatus.Cancelled);

            Assert.That(_service.Enroll(id, _employeeId).IsFailure, Is.True);
        }

        // ---------------------------------------------------------------- outcomes

        [Test]
        public void SetResult_RecordsTheOutcomeAndCertificate()
        {
            long id = _service.Save(NewSession("Sécurité", 0m)).Value;
            _service.Enroll(id, _employeeId);
            long participantId = _service.GetParticipants(id).Single().ParticipantId;

            Result result = _service.SetResult(participantId, TrainingResult.Completed, "16/20", "ATT-2026-01");

            Assert.That(result.IsSuccess, Is.True, result.Error);
            TrainingParticipantSummary p = _service.GetParticipants(id).Single();
            Assert.That(p.Result, Is.EqualTo(TrainingResult.Completed));
            Assert.That(p.CertificateRef, Is.EqualTo("ATT-2026-01"));
            Assert.That(_service.GetSummary(id).CompletedCount, Is.EqualTo(1));
        }

        [Test]
        public void RemoveParticipant_FreesTheEmployeeToBeReEnrolled()
        {
            long id = _service.Save(NewSession("Sécurité", 0m)).Value;
            _service.Enroll(id, _employeeId);
            long participantId = _service.GetParticipants(id).Single().ParticipantId;

            _service.RemoveParticipant(participantId);
            Assert.That(_service.GetParticipants(id).Count, Is.EqualTo(0));

            Assert.That(_service.Enroll(id, _employeeId).IsSuccess, Is.True, "re-enrolment works after removal");
        }

        // ---------------------------------------------------------------- history + listing

        [Test]
        public void GetEmployeeHistory_ListsEverySessionTheEmployeeAttended()
        {
            long a = _service.Save(NewSession("Sécurité", 0m)).Value;
            long b = _service.Save(NewSession("Bureautique", 0m)).Value;
            _service.Enroll(a, _employeeId);
            _service.Enroll(b, _employeeId);
            _service.Enroll(a, _otherEmployeeId);

            IReadOnlyList<TrainingHistoryItem> history = _service.GetEmployeeHistory(_employeeId);

            Assert.That(history.Count, Is.EqualTo(2), "both sessions for this employee, none of the other's");
        }

        [Test]
        public void GetByCompany_ReturnsSessionsWithParticipantCounts()
        {
            long id = _service.Save(NewSession("Sécurité", 50000m)).Value;
            _service.Enroll(id, _employeeId);
            _service.Enroll(id, _otherEmployeeId);
            long pid = _service.GetParticipants(id).First().ParticipantId;
            _service.SetResult(pid, TrainingResult.Completed, null, null);

            IReadOnlyList<TrainingSummary> sessions = _service.GetByCompany(_companyId);

            Assert.That(sessions.Count, Is.EqualTo(1));
            Assert.That(sessions[0].ParticipantCount, Is.EqualTo(2));
            Assert.That(sessions[0].CompletedCount, Is.EqualTo(1));
        }

        [Test]
        public void Delete_RemovesTheSession()
        {
            long id = _service.Save(NewSession("Sécurité", 0m)).Value;

            Assert.That(_service.Delete(id).IsSuccess, Is.True);
            Assert.That(_service.Get(id), Is.Null);
        }

        private TrainingSession NewSession(string title, decimal cost)
        {
            return new TrainingSession
            {
                CompanyId = _companyId,
                Title = title,
                Category = "Sécurité",
                Provider = "Organisme X",
                StartDate = new DateTime(2026, 5, 1),
                EndDate = new DateTime(2026, 5, 3),
                Location = "Alger",
                Cost = cost
            };
        }
    }
}
