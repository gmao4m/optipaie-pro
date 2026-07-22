using System;
using System.Collections.Generic;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Primitives;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>Edit dialog view model for an employee. Binds directly to the entity.</summary>
    public sealed class EmployeeEditViewModel : ObservableObject
    {
        private readonly AppServices _services;
        private readonly bool _isNew;

        public EmployeeEditViewModel(AppServices services, Employee employee, bool isNew)
        {
            _services = services;
            _isNew = isNew;
            Employee = employee;

            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        }

        public Employee Employee { get; }

        public string Title => _isNew ? "Nouvel employé" : "Modifier l'employé";

        public List<EnumOption> Genders { get; } = EnumLabels.Genders();
        public List<EnumOption> Contracts { get; } = EnumLabels.Contracts();
        public List<EnumOption> Maritals { get; } = EnumLabels.Maritals();
        public List<EnumOption> Payments { get; } = EnumLabels.Payments();

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        /// <summary>Set by the dialog host to close the window with a result.</summary>
        public Action<bool> RequestClose { get; set; }

        private void Save()
        {
            if (string.IsNullOrWhiteSpace(Employee.LastNameFr) || string.IsNullOrWhiteSpace(Employee.FirstNameFr))
            {
                Dialogs.Error("Le nom et le prénom sont obligatoires.");
                return;
            }

            if (Employee.CompanyId <= 0)
            {
                Dialogs.Error("L'employé doit être rattaché à une entreprise.");
                return;
            }

            if (Employee.HireDate == default(DateTime))
            {
                Employee.HireDate = DateTime.Today;
            }

            if (_isNew)
            {
                Result<long> result = _services.Employees.Create(Employee);
                if (!result.IsSuccess)
                {
                    Dialogs.Error(result.Error);
                    return;
                }

                // Cross-module link: a new hire automatically gets a matching DRAFT contract
                // (pre-filled from what we already know) so the user finalises it in Contrats
                // instead of re-entering everything. Never blocks saving the employee.
                Result<long> draft = _services.Contracts.CreateDraftFromEmployee(result.Value);
                if (draft.IsSuccess)
                {
                    Dialogs.Info(
                        "Un contrat provisoire a été créé automatiquement pour cet employé.\r\n" +
                        "Ouvrez le module « Contrats » pour compléter et activer ses termes (type, salaire, dates).",
                        "Contrat créé");
                }
            }
            else
            {
                Result result = _services.Employees.Update(Employee);
                if (!result.IsSuccess)
                {
                    Dialogs.Error(result.Error);
                    return;
                }
            }

            RequestClose?.Invoke(true);
        }
    }
}
