using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// Manage the public-holidays calendar one year at a time. Civil holidays can be pre-filled;
    /// religious ones (which move each year) are entered by hand. A holiday inside a leave period is
    /// not decremented from the balance (enforced by the service).
    /// </summary>
    public sealed class HolidaysViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly long _companyId;

        private int _year = DateTime.Today.Year;
        private Holiday _selected;
        private DateTime _newDate = DateTime.Today;
        private string _newName = string.Empty;
        private bool _newReligious = true;
        private string _statusMessage = string.Empty;

        public HolidaysViewModel(AppServices services, long companyId)
        {
            _services = services;
            _companyId = companyId;

            for (int y = DateTime.Today.Year - 3; y <= DateTime.Today.Year + 2; y++) Years.Add(y);

            AddCommand = new RelayCommand(Add);
            DeleteCommand = new RelayCommand(Delete, () => _selected != null);
            PrefillCommand = new RelayCommand(PrefillCivil);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());

            Load();
        }

        public Action RequestClose { get; set; }

        public ObservableCollection<Holiday> Holidays { get; } = new ObservableCollection<Holiday>();
        public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();

        public int Year { get => _year; set { if (Set(ref _year, value)) Load(); } }
        public Holiday SelectedHoliday { get => _selected; set => Set(ref _selected, value); }
        public DateTime NewDate { get => _newDate; set => Set(ref _newDate, value); }
        public string NewName { get => _newName; set => Set(ref _newName, value); }
        public bool NewReligious { get => _newReligious; set => Set(ref _newReligious, value); }
        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

        public ICommand AddCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand PrefillCommand { get; }
        public ICommand CloseCommand { get; }

        private void Load()
        {
            Holidays.Clear();
            foreach (Holiday h in _services.Holidays.GetForYear(_companyId, _year)) Holidays.Add(h);
            StatusMessage = Holidays.Count + " jour(s) férié(s) en " + _year;
        }

        private void Add()
        {
            var holiday = new Holiday
            {
                CompanyId = _companyId,
                HolidayDate = _newDate.Date,
                NameAr = _newName,
                IsReligious = _newReligious
            };

            Result<long> result = _services.Holidays.Add(holiday);
            if (result.IsFailure) { Dialogs.Error(result.Error); return; }

            NewName = string.Empty;
            if (_newDate.Year != _year) Year = _newDate.Year;
            else Load();
            StatusMessage = "Jour férié ajouté.";
        }

        private void Delete()
        {
            if (_selected == null) return;
            _services.Holidays.Delete(_selected.Id);
            Load();
            StatusMessage = "Jour férié supprimé.";
        }

        private void PrefillCivil()
        {
            int added = _services.Holidays.EnsureCivilForYear(_companyId, _year);
            Load();
            StatusMessage = added > 0 ? added + " fête(s) civile(s) ajoutée(s)." : "Les fêtes civiles sont déjà présentes.";
        }
    }
}
