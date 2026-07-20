namespace OptiPaie.Admin.Mvvm
{
    /// <summary>Base for a navigable section; <see cref="Load"/> runs when shown.</summary>
    public abstract class SectionViewModel : ObservableObject
    {
        private bool _busy;
        public bool Busy { get => _busy; protected set { if (Set(ref _busy, value)) Raise(nameof(NotBusy)); } }
        public bool NotBusy => !_busy;

        public virtual void Load() { }
    }
}
