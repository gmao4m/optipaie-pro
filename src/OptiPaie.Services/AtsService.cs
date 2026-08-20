using System;
using System.Collections.Generic;
using System.Linq;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Recruitment orchestration. Postings + a strictly-ordered candidate pipeline
    /// (Reçu→Présélectionné→Entretien→Retenu→Recruté). The pipeline order is enforced HERE,
    /// in the service — the UI only ever offers the single legal next step. Hiring is the
    /// ecosystem link (<see cref="Hire"/>): in ONE atomic transaction it creates the SHARED
    /// employee through the same validated + audited path as any hire, opens a draft contract,
    /// fills the posting when its positions are met, and links the candidate both ways. The
    /// payroll engine is untouched.
    /// </summary>
    public sealed class AtsService : IAtsService
    {
        private readonly IUnitOfWorkFactory _unitOfWorkFactory;
        private readonly IValidator<Employee> _employeeValidator;

        public AtsService(IUnitOfWorkFactory unitOfWorkFactory, IValidator<Employee> employeeValidator)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _employeeValidator = Guard.AgainstNull(employeeValidator, nameof(employeeValidator));
        }

        /// <summary>Audit trail (candidate hired → employee, etc.). Best-effort.</summary>
        public IAuditSink Audit { get; set; } = NullAuditSink.Instance;

        // -- postings ----------------------------------------------------------

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

            if (string.IsNullOrWhiteSpace(posting.Department))
            {
                return Result.Fail<long>("Le département est obligatoire.", "Ats_DepartmentRequired");
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
            RequireCompany(companyId);
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

            if (string.IsNullOrWhiteSpace(candidate.FirstName))
            {
                return Result.Fail<long>("Le prénom du candidat est obligatoire.", "Ats_CandidateFirstNameRequired");
            }

            if (string.IsNullOrWhiteSpace(candidate.Phone))
            {
                return Result.Fail<long>("Le téléphone du candidat est obligatoire.", "Ats_CandidatePhoneRequired");
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

                    // Stage/closure/link are driven by the pipeline actions, never by an edit.
                    candidate.Stage = existing.Stage;
                    candidate.ClosureType = existing.ClosureType;
                    candidate.ClosureReason = existing.ClosureReason;
                    candidate.ClosureDate = existing.ClosureDate;
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

        // -- pipeline (strict, service-enforced) -------------------------------

        /// <summary>The active pipeline stages, in order. Hired/Rejected are terminal (own actions).</summary>
        private const int FirstActive = (int)CandidateStage.Applied;   // 1
        private const int LastActive = (int)CandidateStage.Offer;      // 4

        public Result MoveStage(long candidateId, CandidateStage target)
        {
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

                if (candidate.Stage == CandidateStage.Rejected)
                {
                    return Result.Fail("Ce dossier est clôturé.", "Ats_ClosedFile");
                }

                if (target == CandidateStage.Hired)
                {
                    return Result.Fail("Utilisez « Recruter » pour embaucher un candidat.", "Ats_UseHire");
                }

                if (target == CandidateStage.Rejected)
                {
                    return Result.Fail("Utilisez « Refuser » ou « Désistement ».", "Ats_UseRejectOrDesist");
                }

                int cur = (int)candidate.Stage;
                int tgt = (int)target;
                if (tgt < FirstActive || tgt > LastActive)
                {
                    return Result.Fail("Étape invalide.", "Ats_InvalidStage");
                }

                // Exactly one step forward or back — no skipping.
                if (Math.Abs(tgt - cur) != 1)
                {
                    return Result.Fail("Une étape à la fois.", "Ats_NoSkip");
                }

                candidate.Stage = target;
                uow.Ats.UpdateCandidate(candidate);
                return Result.Ok();
            }
        }

        /// <summary>Advance to the immediate next stage (the single "étape suivante" action).</summary>
        public Result MoveNext(long candidateId)
        {
            Candidate c = GetCandidate(candidateId);
            if (c == null) return Result.Fail("Candidat introuvable.", "Ats_CandidateNotFound");
            return MoveStage(candidateId, (CandidateStage)((int)c.Stage + 1));
        }

        /// <summary>Step back exactly one stage to correct a mistake (UI confirms).</summary>
        public Result MoveBack(long candidateId)
        {
            Candidate c = GetCandidate(candidateId);
            if (c == null) return Result.Fail("Candidat introuvable.", "Ats_CandidateNotFound");
            return MoveStage(candidateId, (CandidateStage)((int)c.Stage - 1));
        }

        public Result Reject(long candidateId, string reason) => Close(candidateId, CandidateClosure.Rejected, reason);

        public Result Desist(long candidateId, string reason) => Close(candidateId, CandidateClosure.Withdrawn, reason);

        private Result Close(long candidateId, CandidateClosure type, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Result.Fail("Le motif est obligatoire.", "Ats_ReasonRequired");
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

                if (candidate.Stage == CandidateStage.Rejected)
                {
                    return Result.Fail("Ce dossier est déjà clôturé.", "Ats_ClosedFile");
                }

                candidate.Stage = CandidateStage.Rejected;
                candidate.ClosureType = type;
                candidate.ClosureReason = reason.Trim();
                candidate.ClosureDate = DateTime.Today;
                uow.Ats.UpdateCandidate(candidate);
                return Result.Ok();
            }
        }

        // -- hire (the atomic conversion) --------------------------------------

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

                // Strict order: a hire only happens from « Retenu ».
                if (candidate.Stage != CandidateStage.Offer)
                {
                    return Result.Fail<HireResult>(
                        "Le candidat doit être à l'étape « Retenu » avant d'être recruté.", "Ats_MustBeOffer");
                }

                JobPosting posting = uow.Ats.GetPostingById(candidate.PostingId);
                if (posting == null)
                {
                    return Result.Fail<HireResult>("Offre introuvable.", "Ats_PostingNotFound");
                }

                uow.BeginTransaction();
                try
                {
                    var employee = new Employee
                    {
                        CompanyId = posting.CompanyId,
                        LastNameFr = candidate.LastName,
                        // Defensive fallback for legacy candidates without a first name (validator requires it).
                        FirstNameFr = string.IsNullOrWhiteSpace(candidate.FirstName) ? candidate.LastName : candidate.FirstName,
                        Gender = Gender.Male,
                        MaritalStatus = MaritalStatus.Single,
                        PaymentMode = PaymentMode.BankTransfer,
                        ContractType = posting.ContractType ?? ContractType.Cdi,
                        Poste = posting.Title,
                        HireDate = DateTime.Today,
                        BaseSalary = 0m,
                        IsActive = true
                    };

                    // SAME validated + audited path as EmployeeService.Create, inside this transaction.
                    Result<long> inserted = EmployeeCreation.InsertValidated(uow, employee, _employeeValidator, Audit);
                    if (inserted.IsFailure)
                    {
                        uow.Rollback();
                        return Result.Fail<HireResult>(inserted.Error, inserted.ErrorCode);
                    }

                    long employeeId = inserted.Value;

                    candidate.Stage = CandidateStage.Hired;
                    candidate.HiredEmployeeId = employeeId;
                    uow.Ats.UpdateCandidate(candidate);

                    // Draft contract, same transaction (activation stays manual).
                    Result<long> contract = EmployeeCreation.InsertDraftContract(uow, employeeId, employee);

                    // Fill the posting when its positions are met.
                    int hired = uow.Ats.GetCandidatesByPosting(posting.Id).Count(c => c.Stage == CandidateStage.Hired);
                    bool filled = false;
                    if (hired >= posting.Positions && posting.Status != JobStatus.Filled)
                    {
                        posting.Status = JobStatus.Filled;
                        uow.Ats.UpdatePosting(posting);
                        filled = true;
                    }

                    Audit.Record("Recruitment", candidateId, AuditAction.StatusChanged,
                        "Candidat recruté → employé #" + employeeId, "Retenu", "Recruté");

                    uow.Commit();
                    return Result.Ok(new HireResult
                    {
                        EmployeeId = employeeId,
                        ContractId = contract.Value,
                        PostingFilled = filled
                    });
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

        public IReadOnlyList<Candidate> GetCandidatesByCompany(long companyId)
        {
            RequireCompany(companyId);
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Ats.GetCandidatesByCompany(companyId).ToList();
            }
        }

        // -- interviews --------------------------------------------------------

        public Result<long> SaveInterview(Interview interview)
        {
            if (interview == null || interview.CandidateId <= 0)
            {
                return Result.Fail<long>("Entretien invalide.", "Ats_InterviewRequired");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (uow.Ats.GetCandidateById(interview.CandidateId) == null)
                {
                    return Result.Fail<long>("Candidat introuvable.", "Ats_CandidateNotFound");
                }

                if (interview.ScheduledDate == default(DateTime)) interview.ScheduledDate = DateTime.Today;
                return Result.Ok(uow.Ats.InsertInterview(interview));
            }
        }

        public IReadOnlyList<Interview> GetInterviews(long candidateId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Ats.GetInterviewsByCandidate(candidateId).ToList();
            }
        }

        public Result DeleteInterview(long interviewId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.Ats.SoftDeleteInterview(interviewId);
                return Result.Ok();
            }
        }

        // -- attachments (metadata; the file itself is copied by the caller) ---

        public Result<long> AddAttachment(CandidateAttachment attachment)
        {
            if (attachment == null || attachment.CandidateId <= 0 || string.IsNullOrWhiteSpace(attachment.FileName))
            {
                return Result.Fail<long>("Pièce jointe invalide.", "Ats_AttachmentRequired");
            }

            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                if (uow.Ats.GetCandidateById(attachment.CandidateId) == null)
                {
                    return Result.Fail<long>("Candidat introuvable.", "Ats_CandidateNotFound");
                }

                if (attachment.AddedAt == default(DateTime)) attachment.AddedAt = DateTime.UtcNow;
                return Result.Ok(uow.Ats.InsertAttachment(attachment));
            }
        }

        public IReadOnlyList<CandidateAttachment> GetAttachments(long candidateId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return uow.Ats.GetAttachmentsByCandidate(candidateId).ToList();
            }
        }

        public Result DeleteAttachment(long attachmentId)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.Ats.SoftDeleteAttachment(attachmentId);
                return Result.Ok();
            }
        }

        // -- internals ---------------------------------------------------------

        private static void RequireCompany(long companyId)
        {
            if (companyId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(companyId), "Une société valide est obligatoire (jamais « toutes »).");
            }
        }

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
