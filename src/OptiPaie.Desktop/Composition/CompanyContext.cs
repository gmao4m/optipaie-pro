using System;
using System.Collections.ObjectModel;
using System.Linq;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.Composition
{
    /// <summary>
    /// The single, app-wide "active company" that the whole UI reads from. The header
    /// selector writes <see cref="Active"/>; every module reads it on activation and the
    /// shell re-activates the visible screen whenever it changes — so there is exactly one
    /// place to choose a company (no per-screen picker) and switching never leaves stale
    /// data from the previous company on screen.
    /// </summary>
    public sealed class CompanyContext : ObservableObject
    {
        private readonly ICompanyService _companies;
        private Company _active;

        public CompanyContext(ICompanyService companies)
        {
            _companies = companies;
        }

        public ObservableCollection<Company> Companies { get; } = new ObservableCollection<Company>();

        /// <summary>Raised when the active company changes to a different company (by id).</summary>
        public event EventHandler ActiveChanged;

        public Company Active
        {
            get => _active;
            set
            {
                long? oldId = _active?.Id;
                long? newId = value?.Id;
                bool referenceChanged = Set(ref _active, value);

                if (referenceChanged)
                {
                    Raise(nameof(HasActive));
                    Raise(nameof(ActiveName));
                    Raise(nameof(ActiveLogo));
                }

                if (oldId != newId)
                {
                    ActiveChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public bool HasActive => _active != null;
        public string ActiveName => _active != null ? _active.NameFr : "—";
        public byte[] ActiveLogo => _active != null ? _active.Logo : null;

        /// <summary>The active company's id, or 0 when none is selected.</summary>
        public long ActiveId => _active != null ? _active.Id : 0L;

        /// <summary>
        /// Returns the active company's id, or THROWS if none is set. Once the app has started,
        /// a company is always active (the startup gate guarantees it and the header selector has
        /// no "none" option), so a screen opening with no active company is a bug — it must fail
        /// loudly, never silently show an empty or cross-company view.
        /// </summary>
        public long RequireActiveId()
        {
            long id = ActiveId;
            if (id <= 0)
            {
                throw new InvalidOperationException(
                    "Aucune société active : un écran ne doit jamais s'ouvrir sans société sélectionnée.");
            }

            return id;
        }

        /// <summary>
        /// (Re)loads the company list from storage and keeps a valid active selection —
        /// preserving the current company if it still exists, otherwise the first one.
        /// </summary>
        public void Reload()
        {
            long? keepId = _active?.Id;

            Companies.Clear();
            foreach (Company c in _companies.GetAll())
            {
                Companies.Add(c);
            }

            Company match = keepId.HasValue ? Companies.FirstOrDefault(c => c.Id == keepId.Value) : null;
            Active = match ?? Companies.FirstOrDefault();
        }
    }
}
