using System.Collections.ObjectModel;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>Manage the catalogue of payroll item types (add / edit / delete).</summary>
    public sealed class PayrollItemsViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private PayrollElement _selected;

        public PayrollItemsViewModel(AppServices services)
        {
            _services = services;
            Items = new ObservableCollection<PayrollElement>();

            NewCommand = new RelayCommand(New);
            EditCommand = new RelayCommand(Edit);
            DeleteCommand = new RelayCommand(Delete);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());

            Load();
        }

        public ObservableCollection<PayrollElement> Items { get; }

        public PayrollElement Selected
        {
            get => _selected;
            set { if (Set(ref _selected, value)) Raise(nameof(HasSelection)); }
        }

        public bool HasSelection => _selected != null;

        public ICommand NewCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CloseCommand { get; }

        public System.Action RequestClose { get; set; }

        private void Load()
        {
            long? keep = _selected?.Id;
            Items.Clear();

            foreach (PayrollElement el in _services.PayrollElements.GetAll(true))
            {
                if (!el.IsDeleted)
                {
                    Items.Add(el);
                }
            }

            foreach (PayrollElement el in Items)
            {
                if (keep.HasValue && el.Id == keep.Value)
                {
                    Selected = el;
                    return;
                }
            }

            Selected = null;
        }

        private void New()
        {
            if (Dialogs.ShowPayrollItemEditor(new PayrollItemEditViewModel(_services, new PayrollElement(), true)))
            {
                Load();
            }
        }

        private void Edit()
        {
            if (Selected == null)
            {
                return;
            }

            PayrollElement full = _services.PayrollElements.Get(Selected.Id);
            if (full == null)
            {
                return;
            }

            if (Dialogs.ShowPayrollItemEditor(new PayrollItemEditViewModel(_services, full, false)))
            {
                Load();
            }
        }

        private void Delete()
        {
            if (Selected == null)
            {
                return;
            }

            if (!Dialogs.Confirm("Voulez-vous vraiment supprimer cette rubrique ?"))
            {
                return;
            }

            Result result = _services.PayrollElements.Delete(Selected.Id);
            if (result.IsSuccess)
            {
                Load();
            }
            else
            {
                Dialogs.Error(result.Error);
            }
        }
    }
}
