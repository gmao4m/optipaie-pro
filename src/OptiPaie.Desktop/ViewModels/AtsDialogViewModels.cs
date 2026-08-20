using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32;
using OptiPaie.Common.Constants;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Localization;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>A contract-type option for the posting editor (folded, optional).</summary>
    public sealed class PostingContractTypeOption
    {
        public PostingContractTypeOption(ContractType? value, string label) { Value = value; Label = label; }
        public ContractType? Value { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }

    /// <summary>Create / edit a job posting. Only 3 fields are required; the rest is folded.</summary>
    public sealed class AtsPostingEditViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly JobPosting _posting;
        private readonly long _companyId;

        private string _title;
        private string _department;
        private string _positions;
        private PostingContractTypeOption _contractType;
        private DateTime? _deadline;
        private string _responsibleName;
        private string _description;

        public AtsPostingEditViewModel(AppServices services, long companyId, JobPosting existing)
        {
            _services = services;
            _companyId = companyId;
            _posting = existing ?? new JobPosting();

            ContractTypes.Add(new PostingContractTypeOption(null, L("Recruit_NotSpecified")));
            ContractTypes.Add(new PostingContractTypeOption(ContractType.Cdi, L("Enum_ContractType_Cdi")));
            ContractTypes.Add(new PostingContractTypeOption(ContractType.Cdd, L("Enum_ContractType_Cdd")));

            if (existing != null)
            {
                _title = existing.Title;
                _department = existing.Department;
                _positions = existing.Positions.ToString(CultureInfo.InvariantCulture);
                _deadline = existing.Deadline;
                _responsibleName = existing.ResponsibleName;
                _description = existing.Description;
                _contractType = ContractTypes.FirstOrDefault(o => o.Value == existing.ContractType) ?? ContractTypes[0];
                HeaderTitle = L("Recruit_EditPosting");
            }
            else
            {
                _positions = "1";
                _contractType = ContractTypes[0];
                HeaderTitle = L("Recruit_NewPosting");
            }

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Action<bool> RequestClose { get; set; }
        public string HeaderTitle { get; }

        public string PostingTitle { get => _title; set => Set(ref _title, value); }
        public string Department { get => _department; set => Set(ref _department, value); }
        public string Positions { get => _positions; set => Set(ref _positions, value); }
        public PostingContractTypeOption ContractTypeSel { get => _contractType; set => Set(ref _contractType, value); }
        public DateTime? Deadline { get => _deadline; set => Set(ref _deadline, value); }
        public string ResponsibleName { get => _responsibleName; set => Set(ref _responsibleName, value); }
        public string Description { get => _description; set => Set(ref _description, value); }

        public ObservableCollection<PostingContractTypeOption> ContractTypes { get; } = new ObservableCollection<PostingContractTypeOption>();

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private void Save()
        {
            int.TryParse(_positions, NumberStyles.Integer, CultureInfo.InvariantCulture, out int positions);
            if (positions < 1) positions = 1;

            _posting.CompanyId = _companyId;
            _posting.Title = _title;
            _posting.Department = _department;
            _posting.Positions = positions;
            _posting.ContractType = _contractType != null ? _contractType.Value : null;
            _posting.Deadline = _deadline;
            _posting.ResponsibleName = _responsibleName;
            _posting.Description = _description;
            if (_posting.Id == 0) _posting.OpenDate = DateTime.Today;

            Result<long> result = _services.Ats.SavePosting(_posting);
            if (result.IsFailure) { Fail(result); return; }
            RequestClose?.Invoke(true);
        }

        private void Fail(Result r) =>
            Dialogs.Error(ResultText.Localize(_services.Localization, r.Error, r.ErrorCode));

        private static string L(string key) => TranslationSource.Instance[key];
    }

    /// <summary>
    /// The candidate fiche: 3 required fields + folded optionals, plus the interviews and CV
    /// attachments (only for a candidate that already exists). No jargon; Arabic messages.
    /// </summary>
    public sealed class AtsCandidateEditViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly long _postingId;
        private readonly long _companyId;
        private Candidate _candidate;

        private string _lastName, _firstName, _phone, _email, _source, _education, _experience, _notes;

        // Inline "add interview" mini-form.
        private DateTime _interviewDate = DateTime.Today;
        private string _interviewType, _interviewer, _interviewResult;

        public AtsCandidateEditViewModel(AppServices services, long postingId, Candidate existing)
        {
            _services = services;
            _postingId = postingId;
            _companyId = services.CompanyContext.Active != null ? services.CompanyContext.Active.Id : 0;
            _candidate = existing ?? new Candidate { PostingId = postingId };

            if (existing != null)
            {
                _lastName = existing.LastName; _firstName = existing.FirstName; _phone = existing.Phone;
                _email = existing.Email; _source = existing.Source; _education = existing.EducationLevel;
                _experience = existing.ExperienceYears?.ToString(CultureInfo.InvariantCulture);
                _notes = existing.Notes;
                HeaderTitle = (existing.LastName + " " + existing.FirstName).Trim();
                LoadDetails();
            }
            else
            {
                HeaderTitle = L("Recruit_NewCandidate");
            }

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
            AddInterviewCommand = new RelayCommand(AddInterview, () => Exists);
            DeleteInterviewCommand = new RelayCommand(p => DeleteInterview(p as Interview));
            AddCvCommand = new RelayCommand(AddCv, () => Exists);
            OpenAttachmentCommand = new RelayCommand(p => OpenAttachment(p as CandidateAttachment));
            DeleteAttachmentCommand = new RelayCommand(p => DeleteAttachment(p as CandidateAttachment));
        }

        public Action<bool> RequestClose { get; set; }
        public string HeaderTitle { get; }

        /// <summary>True once the candidate is saved — interviews / CVs attach to a real row.</summary>
        public bool Exists => _candidate.Id > 0;

        public string LastName { get => _lastName; set => Set(ref _lastName, value); }
        public string FirstName { get => _firstName; set => Set(ref _firstName, value); }
        public string Phone { get => _phone; set => Set(ref _phone, value); }
        public string Email { get => _email; set => Set(ref _email, value); }
        public string Source { get => _source; set => Set(ref _source, value); }
        public string Education { get => _education; set => Set(ref _education, value); }
        public string Experience { get => _experience; set => Set(ref _experience, value); }
        public string Notes { get => _notes; set => Set(ref _notes, value); }

        public DateTime InterviewDate { get => _interviewDate; set => Set(ref _interviewDate, value); }
        public string InterviewType { get => _interviewType; set => Set(ref _interviewType, value); }
        public string Interviewer { get => _interviewer; set => Set(ref _interviewer, value); }
        public string InterviewResult { get => _interviewResult; set => Set(ref _interviewResult, value); }

        public ObservableCollection<Interview> Interviews { get; } = new ObservableCollection<Interview>();
        public ObservableCollection<CandidateAttachment> Attachments { get; } = new ObservableCollection<CandidateAttachment>();

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand AddInterviewCommand { get; }
        public ICommand DeleteInterviewCommand { get; }
        public ICommand AddCvCommand { get; }
        public ICommand OpenAttachmentCommand { get; }
        public ICommand DeleteAttachmentCommand { get; }

        private void LoadDetails()
        {
            Interviews.Clear();
            foreach (Interview i in _services.Ats.GetInterviews(_candidate.Id)) Interviews.Add(i);
            Attachments.Clear();
            foreach (CandidateAttachment a in _services.Ats.GetAttachments(_candidate.Id)) Attachments.Add(a);
        }

        private void Save()
        {
            int.TryParse(_experience, NumberStyles.Integer, CultureInfo.InvariantCulture, out int years);

            _candidate.PostingId = _postingId;
            _candidate.LastName = _lastName;
            _candidate.FirstName = _firstName;
            _candidate.Phone = _phone;
            _candidate.Email = _email;
            _candidate.Source = _source;
            _candidate.EducationLevel = _education;
            _candidate.ExperienceYears = string.IsNullOrWhiteSpace(_experience) ? (int?)null : Math.Max(0, years);
            _candidate.Notes = _notes;
            if (_candidate.Id == 0) _candidate.AppliedDate = DateTime.Today;

            Result<long> result = _services.Ats.SaveCandidate(_candidate);
            if (result.IsFailure) { Fail(result); return; }
            RequestClose?.Invoke(true);
        }

        private void AddInterview()
        {
            var interview = new Interview
            {
                CandidateId = _candidate.Id, ScheduledDate = _interviewDate,
                Type = _interviewType, Interviewer = _interviewer, Result = _interviewResult
            };
            Result<long> r = _services.Ats.SaveInterview(interview);
            if (r.IsFailure) { Fail(r); return; }

            InterviewType = Interviewer = InterviewResult = string.Empty;
            LoadDetails();
        }

        private void DeleteInterview(Interview interview)
        {
            if (interview == null || !Dialogs.Confirm(L("Recruit_ConfirmDeleteInterview"))) return;
            _services.Ats.DeleteInterview(interview.Id);
            LoadDetails();
        }

        private void AddCv()
        {
            var dialog = new OpenFileDialog { Filter = "PDF, Word, images|*.pdf;*.doc;*.docx;*.jpg;*.png|Tous|*.*" };
            if (dialog.ShowDialog() != true) return;

            try
            {
                string relDir = Path.Combine("Recrutement", _companyId.ToString(), _candidate.Id.ToString());
                string absDir = Path.Combine(DataRoot(), relDir);
                Directory.CreateDirectory(absDir);
                string fileName = Path.GetFileName(dialog.FileName);
                string relPath = Path.Combine(relDir, fileName);
                File.Copy(dialog.FileName, Path.Combine(DataRoot(), relPath), overwrite: true);

                Result<long> r = _services.Ats.AddAttachment(new CandidateAttachment
                {
                    CandidateId = _candidate.Id, FileName = fileName, RelativePath = relPath, Kind = "CV", AddedAt = DateTime.UtcNow
                });
                if (r.IsFailure) { Fail(r); return; }
                LoadDetails();
            }
            catch (Exception ex)
            {
                _services.Logger.Warn("Recrutement CV: " + ex.Message);
                Dialogs.Error(L("Recruit_CvError"));
            }
        }

        private void OpenAttachment(CandidateAttachment a)
        {
            if (a == null) return;
            try
            {
                string full = Path.Combine(DataRoot(), a.RelativePath);
                if (File.Exists(full)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(full) { UseShellExecute = true });
                else Dialogs.Info(L("Recruit_CvMissing"));
            }
            catch (Exception ex) { _services.Logger.Warn("Recrutement open CV: " + ex.Message); }
        }

        private void DeleteAttachment(CandidateAttachment a)
        {
            if (a == null || !Dialogs.Confirm(L("Recruit_ConfirmDeleteCv"))) return;
            _services.Ats.DeleteAttachment(a.Id);
            LoadDetails();
        }

        private static string DataRoot() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppConstants.DataFolderName);

        private void Fail(Result r)
        {
            _services.Logger.Warn("Recrutement: " + r.ErrorCode + " — " + r.Error);
            Dialogs.Error(ResultText.Localize(_services.Localization, r.Error, r.ErrorCode));
        }

        private static string L(string key) => TranslationSource.Instance[key];
    }
}
