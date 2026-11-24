using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The declarations hub: readiness, DAC and DAS. Declarations exist only because of the legal
    /// deductions, so when the IRG/CNAS setting is OFF the hub shows a clear notice INSTEAD of the
    /// tabs — it never silently produces a declaration full of zeros.
    /// </summary>
    public sealed class CnasHubViewModel : ObservableObject, IActivable
    {
        private readonly AppServices _services;
        private bool _legalDeductionsDisabled;

        public CnasHubViewModel(AppServices services)
        {
            _services = services;
            Readiness = new CnasReadinessViewModel(services);
            Dac = new CnasDacViewModel(services);
            Das = new CnasDasViewModel(services);
        }

        public CnasReadinessViewModel Readiness { get; }
        public CnasDacViewModel Dac { get; }
        public CnasDasViewModel Das { get; }

        /// <summary>True when the legal deductions are switched off: the notice replaces the tabs.</summary>
        public bool LegalDeductionsDisabled
        {
            get => _legalDeductionsDisabled;
            private set => Set(ref _legalDeductionsDisabled, value);
        }

        public void OnActivated()
        {
            // Fail-safe read: anything but an explicit OFF is ON.
            LegalDeductionsDisabled = _services?.ConfigurationService != null
                && !_services.ConfigurationService.GetLegalDeductionsEnabled();
            if (LegalDeductionsDisabled) return; // nothing to declare — do not build zero declarations

            Readiness.OnActivated();
            Dac.OnActivated();
            Das.OnActivated();
        }
    }
}
