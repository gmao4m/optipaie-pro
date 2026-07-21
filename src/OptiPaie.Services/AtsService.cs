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
    /// Recruitment (ATS) orchestration. Manages job postings and their candidate
    /// pipeline. The ecosystem link is <see cref="Hire"/>: it creates the SHARED employee
    /// for the posting's company inside one transaction, links it to the candidate, and
    /// fills the posting once its positions are met — so a new hire flows straight into
    /// contracts and payroll without any re-entry. The payroll engine is untouched.
    /// </summary>
    public sealed class AtsService : IAtsService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public AtsService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        public Result<long> SavePosting(JobPosting posting)
        {
            if (posting == null)
            {
                return Result.Fail<long>("Aucune offre.", "Ats_PostingRequired");
            }

            if (string.IsNullOrWhiteSpace(posting.Title))
            {
                return Result.Fail<long>("L'intitulé du poste est obligatoire.", "Ats_TitleRequired");
            }

            if (posting.CompanyId <= 0)
            {
                return Result.Fail<long>("Entreprise obligatoire.", "Ats_CompanyRequired");
            }

            if (posting.Positions < 1)
            {
                posting.Positions = 1;
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (posting.Id > 0)
                {
                    JobPosting existing = uow.Ats.GetPostingById(posting.Id);
                    if (existing == null)
                    {
                        return Result.Fail<long>("Offre introuvable.", "Ats_PostingNotFound");
                    }

                    posting.Status = existing.Status;
                    posting.CreatedAtUtc = existing.CreatedAtUtc;
                    uow.Ats.UpdatePosting(posting);
                    return Result.Ok(posting.Id);
                }

                if (posting.OpenDate == default(DateTime)) posting.OpenDate = DateTime.Today;
                posting.Status = JobStatus.Open;
                return Result.Ok(uow.Ats.InsertPosting(posting));
            }
        }

        public Result SetPostingStatus(long postingId, JobStatus status)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                JobPosting posting = uow.Ats.GetPostingById(postingId);
                if (posting == null)
                {
                    return Result.Fail("Offre introuvable.", "Ats_PostingNotFound");
                }

                posting.Status = status;
                uow.Ats.UpdatePosting(posting);
                return Result.Ok();
            }
        }

        public Result DeletePosting(long postingId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.Ats.SoftDeletePosting(postingId);
                return Result.Ok();
            }
        }

        public JobPosting GetPosting(long postingId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Ats.GetPostingById(postingId);
            }
        }

        public JobPostingSummary GetPostingSummary(long postingId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                JobPosting posting = uow.Ats.GetPostingById(postingId);
                return posting == null ? null : Summarise(uow, posting);
            }
        }

        public IReadOnlyList<JobPostingSummary> GetPostingsByCompany(long companyId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Ats.GetPostingsByCompany(companyId).Select(p => Summarise(uow, p)).ToList();
            }
        }

        // -- candidates --------------------------------------------------------

        public Result<long> SaveCandidate(Candidate candidate)
        {
            if (candidate == null)
            {
                return Result.Fail<long>("Aucun candidat.", "Ats_CandidateRequired");
            }

            if (string.IsNullOrWhiteSpace(candidate.LastName))
            {
                return Result.Fail<long>("Le nom du candidat est obligatoire.", "Ats_CandidateNameRequired");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (uow.Ats.GetPostingById(candidate.PostingId) == null)
                {
                    return Result.Fail<long>("Offre introuvable.", "Ats_PostingNotFound");
                }

                if (candidate.Id > 0)
                {
                    Candidate existing = uow.Ats.GetCandidateById(candidate.Id);
                    if (existing == null)
                    {
                        return Result.Fail<long>("Candidat introuvable.", "Ats_CandidateNotFound");
                    }

                    // Hiring is handled by Hire(); an edit never changes the link or a hired stage.
                    candidate.Stage = existing.Stage;
                    candidate.HiredEmployeeId = existing.HiredEmployeeId;
                    candidate.CreatedAtUtc = existing.CreatedAtUtc;
                    uow.Ats.UpdateCandidate(candidate);
                    return Result.Ok(candidate.Id);
                }

                if (candidate.AppliedDate == default(DateTime)) candidate.AppliedDate = DateTime.Today;
                candidate.Stage = CandidateStage.Applied;
                return Result.Ok(uow.Ats.InsertCandidate(candidate));
            }
        }

        public Result MoveStage(long candidateId, CandidateStage stage)
        {
            if (stage == CandidateStage.Hired)
            {
                return Result.Fail("Utilisez « Recruter » pour embaucher un candidat.", "Ats_UseHire");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Candidate candidate = uow.Ats.GetCandidateById(candidateId);
                if (candidate == null)
                {
                    return Result.Fail("Candidat introuvable.", "Ats_CandidateNotFound");
                }

                if (candidate.Stage == CandidateStage.Hired)
                {
                    return Result.Fail("Ce candidat est déjà recruté.", "Ats_AlreadyHired");
                }

                candidate.Stage = stage;
                uow.Ats.UpdateCandidate(candidate);
                return Result.Ok();
            }
        }

        public Result Reject(long candidateId) => MoveStage(candidateId, CandidateStage.Rejected);

        public Result<HireResult> Hire(long candidateId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Candidate candidate = uow.Ats.GetCandidateById(candidateId);
                if (candidate == null)
                {
                    return Result.Fail<HireResult>("Candidat introuvable.", "Ats_CandidateNotFound");
                }

                if (candidate.Stage == CandidateStage.Hired || candidate.HiredEmployeeId.HasValue)
                {
                    return Result.Fail<HireResult>("Ce candidat est déjà recruté.", "Ats_AlreadyHired");
                }

                JobPosting posting = uow.Ats.GetPostingById(candidate.PostingId);
                if (posting == null)
                {
                    return Result.Fail<HireResult>("Offre introuvable.", "Ats_PostingNotFound");
                }

                uow.BeginTransaction();
                try
                {
                    // Create the SHARED employee. Sensible defaults are used for the fields
                    // recruitment doesn't capture; HR completes the record and issues a
                    // contract afterwards. This is the single source of truth — the new
                    // hire is now visible to every other module.
                    long employeeId = uow.Employees.Insert(new Employee
                    {
                        CompanyId = posting.CompanyId,
                        LastNameFr = candidate.LastName,
                        FirstNameFr = candidate.FirstName,
                        Gender = Gender.Male,
                        MaritalStatus = MaritalStatus.Single,
                        PaymentMode = PaymentMode.BankTransfer,
                        ContractType = ContractType.Cdi,
                        Poste = posting.Title,
                        HireDate = DateTime.Today,
                        BaseSalary = 0m,
                        IsActive = true
                    });

                    candidate.Stage = CandidateStage.Hired;
                    candidate.HiredEmployeeId = employeeId;
                    uow.Ats.UpdateCandidate(candidate);

                    // Fill the posting when its positions have been met.
                    int hired = uow.Ats.GetCandidatesByPosting(posting.Id)
                        .Count(c => c.Stage == CandidateStage.Hired);

                    bool filled = false;
                    if (hired >= posting.Positions && posting.Status != JobStatus.Filled)
                    {
                        posting.Status = JobStatus.Filled;
                        uow.Ats.UpdatePosting(posting);
                        filled = true;
                    }

                    uow.Commit();
                    return Result.Ok(new HireResult { EmployeeId = employeeId, PostingFilled = filled });
                }
                catch
                {
                    uow.Rollback();
                    throw;
                }
            }
        }

        public Result DeleteCandidate(long candidateId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                Candidate candidate = uow.Ats.GetCandidateById(candidateId);
                if (candidate == null)
                {
                    return Result.Ok();
                }

                if (candidate.HiredEmployeeId.HasValue)
                {
                    // The employee already exists in the shared table and is managed there;
                    // removing the candidate row must not delete a real employee.
                    return Result.Fail(
                        "Ce candidat a été recruté — gérez l'employé depuis le module Employés.", "Ats_CandidateHired");
                }

                uow.Ats.SoftDeleteCandidate(candidateId);
                return Result.Ok();
            }
        }

        public Candidate GetCandidate(long candidateId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Ats.GetCandidateById(candidateId);
            }
        }

        public IReadOnlyList<Candidate> GetCandidates(long postingId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Ats.GetCandidatesByPosting(postingId).ToList();
            }
        }

        // -- internals ---------------------------------------------------------

        private static JobPostingSummary Summarise(IUnitOfWork uow, JobPosting posting)
        {
            List<Candidate> candidates = uow.Ats.GetCandidatesByPosting(posting.Id).ToList();

            return new JobPostingSummary
            {
                PostingId = posting.Id,
                CompanyId = posting.CompanyId,
                Title = posting.Title,
                Department = posting.Department,
                Status = posting.Status,
                OpenDate = posting.OpenDate,
                Positions = posting.Positions,
                CandidateCount = candidates.Count,
                HiredCount = candidates.Count(c => c.Stage == CandidateStage.Hired)
            };
        }
    }
}
