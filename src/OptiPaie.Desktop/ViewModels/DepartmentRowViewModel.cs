using System;
using System.Windows.Input;
using OptiPaie.Core.Entities;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>One editable department row inside the company editor.</summary>
    public sealed class DepartmentRowViewModel : ObservableObject
    {
        private readonly long _id;
        private readonly long _companyId;
        private readonly int _displayOrder;
        private string _name;

        public DepartmentRowViewModel(Department department,
            Action<DepartmentRowViewModel> rename, Action<DepartmentRowViewModel> remove)
        {
            _id = department.Id;
            _companyId = department.CompanyId;
            _displayOrder = department.DisplayOrder;
            _name = department.Name;

            RenameCommand = new RelayCommand(() => rename(this));
            RemoveCommand = new RelayCommand(() => remove(this));
        }

        public long Id => _id;

        /// <summary>The editable name; committed by <see cref="RenameCommand"/>.</summary>
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; Raise(); } }
        }

        public ICommand RenameCommand { get; }
        public ICommand RemoveCommand { get; }

        public Department ToEntity() => new Department
        {
            Id = _id,
            CompanyId = _companyId,
            Name = _name,
            DisplayOrder = _displayOrder
        };
    }
}
