using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Licensing;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Documents;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Desktop.Views;
using QuestPDF.Fluent;

namespace OptiPaie.Desktop.ViewModels
{
    // ---- section row view-models (all read-only projections) -------------------

    public sealed class ProfileContractRow
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        public ProfileContractRow(ContractSummary s) { S = s; }
        public ContractSummary S { get; }
        public string TypeLabel => EnumLabels.ContractLabel(S.Type);
        public string TypeKind => S.Type == ContractType.Cdi ? "accent" : S.Type == ContractType.Cdd ? "pending" : "neutral";
        public string StatusLabel => ContractLabels.Status(S.Status);
        public string StatusKind =>
            S.Status == ContractStatus.Active ? "success" :
            S.Status == ContractStatus.Expired ? "danger" :
            S.Status == ContractStatus.Renewed ? "accent" : "neutral";
        public string Position => S.Position;
        public string SalaryText => S.BaseSalary.ToString("N2", Fr);
        public string PeriodText => S.StartDate.ToString("dd/MM/yyyy", Fr) + " → " +
                                    (S.EndDate.HasValue ? S.EndDate.Value.ToString("dd/MM/yyyy", Fr) : "—");
        public bool IsCurrent => S.Status == ContractStatus.Active;
    }

    public sealed class ProfilePayslipRow
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        public ProfilePayslipRow(long payslipId, int year, int month, decimal net)
        { PayslipId = payslipId; Year = year; Month = month; NetText = net.ToString("N2", Fr) + " DA"; }
        public long PayslipId { get; }
        public int Year { get; }
        public int Month { get; }
        public string PeriodText => (Month >= 1 && Month <= 12 ? Fr.DateTimeFormat.GetMonthName(Month) : Month.ToString()) + " " + Year;
        public string NetText { get; }
    }

    public sealed class ProfileLeaveRow
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        public ProfileLeaveRow(LeaveRequest r) { R = r; }
        public LeaveRequest R { get; }
        public string TypeLabel => LeaveLabels.Type(R.Type);
        public string PeriodText => R.StartDate.ToString("dd/MM/yyyy", Fr) + " → " + R.EndDate.ToString("dd/MM/yyyy", Fr);
        public string DaysText => R.Days.ToString("0.##", Fr);
        public string StatusLabel => LeaveLabels.Status(R.Status);
        public string StatusKind =>
            R.Status == LeaveStatus.Approved ? "success" :
            R.Status == LeaveStatus.Pending ? "pending" :
            R.Status == LeaveStatus.Rejected ? "danger" : "neutral";
    }

    public sealed class ProfileLoanRow
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        public ProfileLoanRow(LoanSummary s) { S = s; }
        public LoanSummary S { get; }
        public string PrincipalText => S.Principal.ToString("N2", Fr);
        public string OutstandingText => S.Outstanding.ToString("N2", Fr);
        public string InstallmentText => S.MonthlyInstallment.ToString("N2", Fr);
        public string RemainingText => S.RemainingInstallments + " × ";
        public string StatusLabel => LoanLabels.Status(S.Status);
        public string StatusKind =>
            S.Status == LoanStatus.Active ? "accent" :
            S.Status == LoanStatus.Settled ? "success" :
            S.Status == LoanStatus.Suspended ? "pending" : "neutral";
    }

    public sealed class ProfileAssetRow
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        public ProfileAssetRow(AssetAssignmentSummary a) { A = a; }
        public AssetAssignmentSummary A { get; }
        public string AssetName => A.AssetName;
        public string AssignedText => A.AssignedDate.ToString("dd/MM/yyyy", Fr);
        public string ReturnedText => A.ReturnedDate.HasValue ? A.ReturnedDate.Value.ToString("dd/MM/yyyy", Fr) : "—";
        public bool IsOpen => A.ReturnedDate == null;
        public string StatusLabel => IsOpen ? "Détenu" : "Rendu";
        public string StatusKind => IsOpen ? "accent" : "neutral";
    }

    public sealed class ProfileTrainingRow
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        public ProfileTrainingRow(TrainingHistoryItem t) { T = t; }
        public TrainingHistoryItem T { get; }
        public string Title => T.Title;
        public string Provider => T.Provider;
        public string DateText => T.StartDate.ToString("dd/MM/yyyy", Fr);
        public string ResultLabel => TrainingLabels.Result(T.Result);
        public string CertificateText => string.IsNullOrWhiteSpace(T.CertificateRef) ? "—" : T.CertificateRef;
        public string StatusKind =>
            T.Result == TrainingResult.Completed ? "success" :
            T.Result == TrainingResult.Failed ? "danger" : "neutral";
    }

    public sealed class ProfileGoalRow
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        public ProfileGoalRow(GoalRow g) { G = g; }
        public GoalRow G { get; }
        public string Title => G.Title;
        public string ProgressText => G.ProgressPercent.ToString("0.#", Fr) + " %";
        public double ProgressValue => (double)G.ProgressPercent;
        public string StatusLabel => G.StatusLabel;
        public string StatusKind =>
            G.Status == PerformanceGoalStatus.Achieved ? "success" :
            G.IsOverdue ? "danger" : "pending";
    }

    public sealed class ProfileCareerRow
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        public ProfileCareerRow(CareerTimelineItem i) { I = i; }
        public CareerTimelineItem I { get; }
        public string DateText => I.Date.ToString("dd/MM/yyyy", Fr);
        public string Title => I.Title;
        public string Detail => I.Detail;
        public string ValueText => I.ValueText;
    }

    /// <summary>
    /// The 360° employee profile — a read-only hub that pulls every module's data for one
    /// person live through the existing services. It stores nothing; each open re-reads.
    /// </summary>
    public sealed class EmployeeProfileViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly AppServices _services;
        private readonly long _employeeId;
        private readonly Action<string> _navigate;
        private readonly FicheService _fiche = new FicheService();

        private Employee _employee;
        private Company _company;

        public EmployeeProfileViewModel(AppServices services, long employeeId, Action<string> navigate)
        {
            _services = services;
            _employeeId = employeeId;
            _navigate = navigate;

            EditCommand = new RelayCommand(Edit);
            ExportCommand = new RelayCommand(ExportDossier);
            ReopenPayslipCommand = new RelayCommand(p => ReopenPayslip(p as ProfilePayslipRow));
            GoContractsCommand = new RelayCommand(() => Go(ModuleKeys.Contracts));
            GoAttendanceCommand = new RelayCommand(() => Go(ModuleKeys.Attendance));
            GoLeaveCommand = new RelayCommand(() => Go(ModuleKeys.Leave));
            GoLoansCommand = new RelayCommand(() => Go(ModuleKeys.Loans));
            GoAssetsCommand = new RelayCommand(() => Go(ModuleKeys.Assets));
            GoTrainingCommand = new RelayCommand(() => Go(ModuleKeys.Training));
            GoPerformanceCommand = new RelayCommand(() => Go(ModuleKeys.Performance));
            GoPayrollCommand = new RelayCommand(() => Go("archive"));

            Load();
        }

        // -- collections (live projections) ----------------------------------
        public ObservableCollection<ProfileContractRow> Contracts { get; } = new ObservableCollection<ProfileContractRow>();
        public ObservableCollection<ProfilePayslipRow> Payslips { get; } = new ObservableCollection<ProfilePayslipRow>();
        public ObservableCollection<ProfileLeaveRow> LeaveRequests { get; } = new ObservableCollection<ProfileLeaveRow>();
        public ObservableCollection<ProfileLoanRow> Loans { get; } = new ObservableCollection<ProfileLoanRow>();
        public ObservableCollection<ProfileAssetRow> Assets { get; } = new ObservableCollection<ProfileAssetRow>();
        public ObservableCollection<ProfileTrainingRow> Training { get; } = new ObservableCollection<ProfileTrainingRow>();
        public ObservableCollection<ProfileGoalRow> Goals { get; } = new ObservableCollection<ProfileGoalRow>();
        public ObservableCollection<ProfileCareerRow> Career { get; } = new ObservableCollection<ProfileCareerRow>();

        // -- header ----------------------------------------------------------
        public string FullName { get; private set; } = "—";
        public string Initials { get; private set; } = "?";
        public string Poste { get; private set; }
        public string Department { get; private set; }
        public string HireDateText { get; private set; }
        public string Matricule { get; private set; }
        public string ContractTypeLabel { get; private set; }
        public string ContractTypeKind { get; private set; } = "neutral";
        public string StatusLabel { get; private set; }
        public string StatusKind { get; private set; } = "neutral";

        // -- attendance summary (current month) ------------------------------
        public string AttendancePeriod { get; private set; }
        public string PresentText { get; private set; } = "0";
        public string AbsentText { get; private set; } = "0";
        public string LateText { get; private set; } = "0";
        public string OnLeaveText { get; private set; } = "0";

        // -- leave balance ---------------------------------------------------
        public string LeaveEntitlementText { get; private set; } = "0";
        public string LeaveTakenText { get; private set; } = "0";
        public string LeaveRemainingText { get; private set; } = "0";
        public string LeavePendingText { get; private set; } = "0";

        // -- performance snapshot --------------------------------------------
        public string LatestScoreText { get; private set; } = "—";
        public string LatestRatingText { get; private set; }
        public string LatestReviewPeriod { get; private set; }
        public bool HasReview { get; private set; }

        // -- empty-state flags ----------------------------------------------
        public bool HasContracts => Contracts.Count > 0;
        public bool HasPayslips => Payslips.Count > 0;
        public bool HasLeave => LeaveRequests.Count > 0;
        public bool HasLoans => Loans.Count > 0;
        public bool HasAssets => Assets.Count > 0;
        public bool HasTraining => Training.Count > 0;
        public bool HasGoals => Goals.Count > 0;
        public bool HasCareer => Career.Count > 0;
        public bool HasPerformance => HasReview || HasGoals || HasCareer;

        public ICommand EditCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ReopenPayslipCommand { get; }
        public ICommand GoContractsCommand { get; }
        public ICommand GoAttendanceCommand { get; }
        public ICommand GoLeaveCommand { get; }
        public ICommand GoLoansCommand { get; }
        public ICommand GoAssetsCommand { get; }
        public ICommand GoTrainingCommand { get; }
        public ICommand GoPerformanceCommand { get; }
        public ICommand GoPayrollCommand { get; }

        public Action RequestClose { get; set; }

        // ------------------------------------------------------------ loading
        private void Load()
        {
            _employee = _services.Employees.Get(_employeeId);
            if (_employee == null) return;
            _company = _services.Companies.Get(_employee.CompanyId);

            LoadHeader();
            LoadContracts();
            LoadPayroll();
            LoadAttendance();
            LoadLeave();
            LoadLoans();
            LoadAssets();
            LoadTraining();
            LoadPerformance();
        }

        private void LoadHeader()
        {
            FullName = (_employee.LastNameFr + " " + _employee.FirstNameFr).Trim();
            Initials = Initial(_employee.LastNameFr) + Initial(_employee.FirstNameFr);
            Poste = Blank(_employee.Poste);
            Department = Blank(_employee.Department);
            Matricule = _employee.Id.ToString("0000", CultureInfo.InvariantCulture);
            HireDateText = _employee.HireDate.ToString("dd/MM/yyyy", Fr);
            ContractTypeLabel = EnumLabels.ContractLabel(_employee.ContractType);
            ContractTypeKind = _employee.ContractType == ContractType.Cdi ? "accent"
                : _employee.ContractType == ContractType.Cdd ? "pending" : "neutral";

            bool exited = !_employee.IsActive || _employee.ExitDate.HasValue;
            bool onLeave = _services.Leave.GetByEmployee(_employeeId).Any(r =>
                r.Status == LeaveStatus.Approved && r.StartDate.Date <= DateTime.Today && r.EndDate.Date >= DateTime.Today);
            if (exited) { StatusLabel = "Sorti"; StatusKind = "danger"; }
            else if (onLeave) { StatusLabel = "En congé aujourd'hui"; StatusKind = "pending"; }
            else { StatusLabel = "Actif"; StatusKind = "success"; }
        }

        private void LoadContracts()
        {
            foreach (ContractSummary c in _services.Contracts.GetByEmployee(_employeeId)
                         .OrderByDescending(x => x.StartDate))
            {
                Contracts.Add(new ProfileContractRow(c));
            }
        }

        private void LoadPayroll()
        {
            var runPeriod = new Dictionary<long, PayrollRun>();
            var rows = new List<ProfilePayslipRow>();
            foreach (Payslip p in _services.Archive.GetPayslipsByEmployee(_employeeId))
            {
                if (!runPeriod.TryGetValue(p.RunId, out PayrollRun run))
                {
                    run = _services.Archive.GetRun(p.RunId);
                    runPeriod[p.RunId] = run;
                }
                int y = run != null ? run.PeriodYear : 0;
                int m = run != null ? run.PeriodMonth : 0;
                rows.Add(new ProfilePayslipRow(p.Id, y, m, p.NetSalaire));
            }

            foreach (ProfilePayslipRow r in rows.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).Take(6))
            {
                Payslips.Add(r);
            }
        }

        private void LoadAttendance()
        {
            DateTime today = DateTime.Today;
            AttendancePeriod = Fr.DateTimeFormat.GetMonthName(today.Month) + " " + today.Year;
            AttendanceSummary s = _services.Attendance.GetMonthlySummary(_employeeId, today.Year, today.Month);
            if (s != null)
            {
                PresentText = s.PresentDays.ToString();
                AbsentText = s.AbsentDays.ToString();
                LateText = s.LateCount.ToString();
                OnLeaveText = s.LeaveDays.ToString();
            }
        }

        private void LoadLeave()
        {
            LeaveBalance b = _services.Leave.GetBalance(_employeeId, DateTime.Today.Year);
            if (b != null)
            {
                LeaveEntitlementText = b.Entitlement.ToString("0.##", Fr);
                LeaveTakenText = b.Taken.ToString("0.##", Fr);
                LeaveRemainingText = b.Remaining.ToString("0.##", Fr);
                LeavePendingText = b.Pending.ToString("0.##", Fr);
            }

            foreach (LeaveRequest r in _services.Leave.GetByEmployee(_employeeId)
                         .OrderByDescending(x => x.StartDate).Take(8))
            {
                LeaveRequests.Add(new ProfileLeaveRow(r));
            }
        }

        private void LoadLoans()
        {
            foreach (LoanSummary l in _services.Loans.GetByEmployee(_employeeId)
                         .OrderByDescending(x => x.Status == LoanStatus.Active))
            {
                Loans.Add(new ProfileLoanRow(l));
            }
        }

        private void LoadAssets()
        {
            foreach (AssetAssignmentSummary a in _services.Assets.GetAssignmentHistoryByEmployee(_employeeId))
            {
                Assets.Add(new ProfileAssetRow(a));
            }
        }

        private void LoadTraining()
        {
            foreach (TrainingHistoryItem t in _services.Training.GetEmployeeHistory(_employeeId))
            {
                Training.Add(new ProfileTrainingRow(t));
            }
        }

        private void LoadPerformance()
        {
            PerformanceSummary latest = _services.Performance.GetByEmployee(_employeeId)
                .OrderByDescending(r => r.ReviewDate).FirstOrDefault();
            if (latest != null && latest.Status == PerformanceStatus.Completed)
            {
                HasReview = true;
                LatestScoreText = latest.OverallScore.ToString("0.##", Fr);
                LatestRatingText = latest.Rating;
                LatestReviewPeriod = latest.PeriodLabel;
            }

            foreach (GoalRow g in _services.Performance.GetGoals(_employeeId)
                         .Where(x => x.Status != PerformanceGoalStatus.Achieved || x.ProgressPercent < 100m))
            {
                Goals.Add(new ProfileGoalRow(g));
            }

            CareerTimeline timeline = _services.Performance.GetCareerTimeline(_employeeId);
            if (timeline != null)
            {
                foreach (CareerTimelineItem i in timeline.Items) Career.Add(new ProfileCareerRow(i));
            }
        }

        // ------------------------------------------------------------ actions
        private void Edit()
        {
            Employee full = _services.Employees.Get(_employeeId);
            if (full == null) return;
            var vm = new EmployeeEditViewModel(_services, full, false);
            if (Dialogs.ShowEmployeeEditor(vm))
            {
                // Re-read so the profile reflects the edit immediately.
                Reload();
            }
        }

        private void Reload()
        {
            Contracts.Clear(); Payslips.Clear(); LeaveRequests.Clear(); Loans.Clear();
            Assets.Clear(); Training.Clear(); Goals.Clear(); Career.Clear();
            Load();
            RaiseAll();
        }

        private void ReopenPayslip(ProfilePayslipRow row)
        {
            if (row == null) return;
            Payslip payslip = _services.Archive.GetPayslip(row.PayslipId);
            if (payslip == null) { Dialogs.Error("Ce bulletin est introuvable."); return; }
            try
            {
                FichePaieModel model = _fiche.FromPayslip(_company, _employee, payslip,
                    _services.Localization.IsRightToLeft, row.Year, row.Month);
                _fiche.Preview(model);
            }
            catch (Exception ex) { Dialogs.Error("Impossible d'ouvrir le bulletin : " + ex.Message); }
        }

        private void Go(string moduleKey)
        {
            // Close the profile, then hand off to the owning module on the next dispatcher
            // tick so the shell has fully returned from the modal before it re-navigates.
            RequestClose?.Invoke();
            if (_navigate == null) return;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => _navigate(moduleKey)));
        }

        private void ExportDossier()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Document PDF (*.pdf)|*.pdf",
                FileName = "Dossier_" + new string(FullName.Where(char.IsLetterOrDigit).ToArray()) + ".pdf"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var doc = new EmployeeDossierDocument(BuildDossierData());
                QuestPDF.Fluent.Document.Create(doc.Compose).GeneratePdf(dialog.FileName);
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); }
                catch { /* opening is best-effort */ }
            }
            catch (Exception ex)
            {
                _services.Logger.Error("Export dossier employé", ex);
                Dialogs.Error("Impossible d'exporter le dossier : " + ex.Message);
            }
        }

        /// <summary>The flattened dossier snapshot (also used to render/verify the PDF).</summary>
        public EmployeeDossier BuildDossierData()
        {
            return new EmployeeDossier
            {
                CompanyName = _company != null ? _company.NameFr : "—",
                FullName = FullName,
                Poste = Poste,
                Department = Department,
                Matricule = Matricule,
                HireDate = HireDateText,
                ContractType = ContractTypeLabel,
                Status = StatusLabel,
                AttendancePeriod = AttendancePeriod,
                Present = PresentText, Absent = AbsentText, Late = LateText, OnLeave = OnLeaveText,
                LeaveEntitlement = LeaveEntitlementText, LeaveTaken = LeaveTakenText,
                LeaveRemaining = LeaveRemainingText, LeavePending = LeavePendingText,
                LatestScore = HasReview ? (LatestScoreText + (string.IsNullOrEmpty(LatestRatingText) ? "" : " (" + LatestRatingText + ")")) : null,
                Contracts = Contracts.Select(c => new[] { c.TypeLabel, c.Position, c.SalaryText, c.PeriodText, c.StatusLabel }).ToList(),
                Payslips = Payslips.Select(p => new[] { p.PeriodText, p.NetText }).ToList(),
                Leave = LeaveRequests.Select(l => new[] { l.TypeLabel, l.PeriodText, l.DaysText, l.StatusLabel }).ToList(),
                Loans = Loans.Select(l => new[] { l.PrincipalText, l.InstallmentText, l.OutstandingText, l.StatusLabel }).ToList(),
                Assets = Assets.Select(a => new[] { a.AssetName, a.AssignedText, a.ReturnedText, a.StatusLabel }).ToList(),
                Training = Training.Select(t => new[] { t.Title, t.Provider, t.DateText, t.ResultLabel, t.CertificateText }).ToList(),
                Goals = Goals.Select(g => new[] { g.Title, g.ProgressText, g.StatusLabel }).ToList(),
                Career = Career.Select(c => new[] { c.DateText, c.Title, c.Detail }).ToList()
            };
        }

        private void RaiseAll()
        {
            foreach (string p in new[]
            {
                nameof(FullName), nameof(Initials), nameof(Poste), nameof(Department), nameof(HireDateText),
                nameof(Matricule), nameof(ContractTypeLabel), nameof(ContractTypeKind), nameof(StatusLabel), nameof(StatusKind),
                nameof(AttendancePeriod), nameof(PresentText), nameof(AbsentText), nameof(LateText), nameof(OnLeaveText),
                nameof(LeaveEntitlementText), nameof(LeaveTakenText), nameof(LeaveRemainingText), nameof(LeavePendingText),
                nameof(LatestScoreText), nameof(LatestRatingText), nameof(LatestReviewPeriod), nameof(HasReview),
                nameof(HasContracts), nameof(HasPayslips), nameof(HasLeave), nameof(HasLoans),
                nameof(HasAssets), nameof(HasTraining), nameof(HasGoals), nameof(HasCareer), nameof(HasPerformance)
            }) Raise(p);
        }

        private static string Blank(string v) => string.IsNullOrWhiteSpace(v) ? "—" : v;
        private static string Initial(string s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim().Substring(0, 1).ToUpperInvariant();
    }
}
