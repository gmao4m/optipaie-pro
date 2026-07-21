using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Training orchestration. Owns the session lifecycle and the enrolment rules — one
    /// enrolment per employee per session — and resolves participants through the shared
    /// Employees table so no employee data is duplicated.
    /// </summary>
    public sealed class TrainingService : ITrainingService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public TrainingService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        public Result<long> Save(TrainingSession session)
        {
            if (session == null)
            {
                return Result.Fail<long>("Aucune formation.", "Training_Required");
            }

            if (string.IsNullOrWhiteSpace(session.Title))
            {
                return Result.Fail<long>("L'intitulé de la formation est obligatoire.", "Training_TitleRequired");
            }

            if (session.CompanyId <= 0)
            {
                return Result.Fail<long>("Entreprise obligatoire.", "Training_CompanyRequired");
            }

            if (session.StartDate == default(DateTime))
            {
                return Result.Fail<long>("La date de début est obligatoire.", "Training_StartRequired");
            }

            if (session.EndDate.HasValue && session.EndDate.Value.Date < session.StartDate.Date)
            {
                return Result.Fail<long>("La date de fin doit suivre la date de début.", "Training_EndBeforeStart");
            }

            if (session.Cost < 0m)
            {
                return Result.Fail<long>("Le coût ne peut pas être négatif.", "Training_CostInvalid");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (session.Id > 0)
                {
                    TrainingSession existing = uow.Training.GetById(session.Id);
                    if (existing == null)
                    {
                        return Result.Fail<long>("Formation introuvable.", "Training_NotFound");
                    }

                    // The status is driven by SetStatus, not by an edit.
                    session.Status = existing.Status;
                    session.CreatedAtUtc = existing.CreatedAtUtc;
                    uow.Training.Update(session);
                    return Result.Ok(session.Id);
                }

                session.Status = TrainingStatus.Planned;
                return Result.Ok(uow.Training.Insert(session));
            }
        }

        public Result SetStatus(long sessionId, TrainingStatus status)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                TrainingSession session = uow.Training.GetById(sessionId);
                if (session == null)
                {
                    return Result.Fail("Formation introuvable.", "Training_NotFound");
                }

                session.Status = status;
                uow.Training.Update(session);
                return Result.Ok();
            }
        }

        public Result Delete(long sessionId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.Training.SoftDelete(sessionId);
                return Result.Ok();
            }
        }

        public TrainingSession Get(long sessionId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Training.GetById(sessionId);
            }
        }

        public TrainingSummary GetSummary(long sessionId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                TrainingSession session = uow.Training.GetById(sessionId);
                return session == null ? null : Summarise(uow, session);
            }
        }

        public IReadOnlyList<TrainingSummary> GetByCompany(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Training.GetByCompany(companyId).Select(s => Summarise(uow, s)).ToList();
            }
        }

        // -- participants ------------------------------------------------------

        public Result Enroll(long sessionId, long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                TrainingSession session = uow.Training.GetById(sessionId);
                if (session == null)
                {
                    return Result.Fail("Formation introuvable.", "Training_NotFound");
                }

                if (session.Status == TrainingStatus.Cancelled)
                {
                    return Result.Fail("Cette formation est annulée.", "Training_Cancelled");
                }

                if (!uow.Employees.ExistsById(employeeId))
                {
                    return Result.Fail("Employé introuvable.", "Training_EmployeeNotFound");
                }

                if (uow.Training.GetParticipant(sessionId, employeeId) != null)
                {
                    return Result.Fail("Cet employé est déjà inscrit à cette formation.", "Training_AlreadyEnrolled");
                }

                uow.Training.InsertParticipant(new TrainingParticipant
                {
                    SessionId = sessionId,
                    EmployeeId = employeeId,
                    Result = TrainingResult.Enrolled
                });

                return Result.Ok();
            }
        }

        public Result SetResult(long participantId, TrainingResult result, string score, string certificateRef)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                TrainingParticipant participant = uow.Training.GetParticipantById(participantId);
                if (participant == null)
                {
                    return Result.Fail("Participant introuvable.", "Training_ParticipantNotFound");
                }

                participant.Result = result;
                participant.Score = score;
                participant.CertificateRef = certificateRef;
                uow.Training.UpdateParticipant(participant);
                return Result.Ok();
            }
        }

        public Result RemoveParticipant(long participantId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.Training.DeleteParticipant(participantId);
                return Result.Ok();
            }
        }

        public IReadOnlyList<TrainingParticipantSummary> GetParticipants(long sessionId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Training.GetParticipants(sessionId)
                    .Select(p => new TrainingParticipantSummary
                    {
                        ParticipantId = p.Id,
                        SessionId = p.SessionId,
                        EmployeeId = p.EmployeeId,
                        EmployeeName = EmployeeName(uow, p.EmployeeId),
                        Result = p.Result,
                        Score = p.Score,
                        CertificateRef = p.CertificateRef
                    })
                    .ToList();
            }
        }

        public IReadOnlyList<TrainingHistoryItem> GetEmployeeHistory(long employeeId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                var items = new List<TrainingHistoryItem>();
                foreach (TrainingParticipant p in uow.Training.GetByEmployee(employeeId))
                {
                    TrainingSession session = uow.Training.GetById(p.SessionId);
                    if (session == null) continue;

                    items.Add(new TrainingHistoryItem
                    {
                        SessionId = session.Id,
                        Title = session.Title,
                        Provider = session.Provider,
                        StartDate = session.StartDate,
                        Result = p.Result,
                        CertificateRef = p.CertificateRef
                    });
                }

                return items.OrderByDescending(i => i.StartDate).ToList();
            }
        }

        // -- internals ---------------------------------------------------------

        private static TrainingSummary Summarise(IUnitOfWork uow, TrainingSession session)
        {
            List<TrainingParticipant> participants = uow.Training.GetParticipants(session.Id).ToList();

            return new TrainingSummary
            {
                SessionId = session.Id,
                CompanyId = session.CompanyId,
                Title = session.Title,
                Category = session.Category,
                Provider = session.Provider,
                Status = session.Status,
                StartDate = session.StartDate,
                EndDate = session.EndDate,
                Cost = session.Cost,
                ParticipantCount = participants.Count,
                CompletedCount = participants.Count(p => p.Result == TrainingResult.Completed)
            };
        }

        private static string EmployeeName(IUnitOfWork uow, long employeeId)
        {
            Employee employee = uow.Employees.GetById(employeeId);
            return employee == null ? null : (employee.LastNameFr + " " + employee.FirstNameFr).Trim();
        }
    }
}
