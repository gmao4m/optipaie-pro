using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;
using OptiPaie.Desktop.ViewModels.Performance;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// Root of the Performance (Évaluation) module — a four-tab hub: an Overview control centre,
    /// the evaluations board, the department templates, and the reports. The shell resolves this
    /// VM (module key "performance") and shows it via the DataTemplate → PerformanceView.
    /// </summary>
    public sealed class PerformanceViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;
        private int _tab;

        public PerformanceViewModel(AppServices services)
        {
            _services = services;
            Evaluations = new EvaluationsTabViewModel(services);
            Templates = new TemplatesTabViewModel(services);
            Reports = new ReportsTabViewModel(services);
            Overview = new OverviewTabViewModel(services, this);
        }

        public OverviewTabViewModel Overview { get; }
        public EvaluationsTabViewModel Evaluations { get; }
        public TemplatesTabViewModel Templates { get; }
        public ReportsTabViewModel Reports { get; }

        /// <summary>Bound to the hub TabControl. 0 Overview · 1 Evaluations · 2 Templates · 3 Reports.</summary>
        public int SelectedTabIndex { get => _tab; set => Set(ref _tab, value); }

        public void OnActivated()
        {
            long companyId = _services.CompanyContext.Active == null ? 0 : _services.CompanyContext.Active.Id;
            Evaluations.SetCompany(companyId);
            Templates.SetCompany(companyId);
            Reports.SetCompany(companyId);
            Overview.SetCompany(companyId);
        }

        /// <summary>Jump to the evaluations board with a period selected (from an Overview action).</summary>
        public void GoToPeriod(long periodId)
        {
            Evaluations.SelectPeriod(periodId);
            SelectedTabIndex = 1;
        }

        /// <summary>Jump to the per-employee report (from a decline alert's "handle" action).</summary>
        public void GoToReportEmployee(long employeeId)
        {
            Reports.ShowEmployee(employeeId);
            SelectedTabIndex = 3;
        }
    }
}
