using System.Collections.Generic;
using System.Globalization;
using OptiPaie.Core.Entities;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels.Attendance
{
    /// <summary>
    /// One employee row of the matrix: the frozen identity columns plus the day cells
    /// (index 0 = day 1). Selection is bound so bulk operations act on the checked rows.
    /// </summary>
    public sealed class MatrixRowViewModel : ObservableObject
    {
        private bool _isSelected;

        public MatrixRowViewModel(Employee employee, IReadOnlyList<MatrixCellViewModel> cells)
        {
            EmployeeId = employee.Id;
            Number = employee.Id.ToString("0000", CultureInfo.InvariantCulture);
            Name = (employee.LastNameFr + " " + employee.FirstNameFr).Trim();
            Department = string.IsNullOrWhiteSpace(employee.Department) ? "—" : employee.Department;
            Position = string.IsNullOrWhiteSpace(employee.Poste) ? "—" : employee.Poste;
            Cells = cells;
        }

        public long EmployeeId { get; }
        public string Number { get; }
        public string Name { get; }
        public string Department { get; }
        public string Position { get; }

        /// <summary>Day cells for the selected month (Cells[d-1] is day d).</summary>
        public IReadOnlyList<MatrixCellViewModel> Cells { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }
}
