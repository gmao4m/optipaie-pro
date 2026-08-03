using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// « Déclarations CNAS » hub — hosts the two read-only tabs: the data-readiness check
    /// (tranche 1) and the DAC recap (tranche 2). The DAS will join here later.
    /// </summary>
    public sealed class CnasHubViewModel : ObservableObject, IActivable
    {
        public CnasHubViewModel(AppServices services)
        {
            Readiness = new CnasReadinessViewModel(services);
            Dac = new CnasDacViewModel(services);
        }

        public CnasReadinessViewModel Readiness { get; }
        public CnasDacViewModel Dac { get; }

        public void OnActivated()
        {
            Readiness.OnActivated();
            Dac.OnActivated();
        }
    }
}
