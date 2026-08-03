using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// « Déclarations CNAS » hub — hosts the three tabs: the data-readiness check (tranche 1),
    /// the DAC recap (tranche 2), and the annual DAS file generation (tranche 3).
    /// </summary>
    public sealed class CnasHubViewModel : ObservableObject, IActivable
    {
        public CnasHubViewModel(AppServices services)
        {
            Readiness = new CnasReadinessViewModel(services);
            Dac = new CnasDacViewModel(services);
            Das = new CnasDasViewModel(services);
        }

        public CnasReadinessViewModel Readiness { get; }
        public CnasDacViewModel Dac { get; }
        public CnasDasViewModel Das { get; }

        public void OnActivated()
        {
            Readiness.OnActivated();
            Dac.OnActivated();
            Das.OnActivated();
        }
    }
}
