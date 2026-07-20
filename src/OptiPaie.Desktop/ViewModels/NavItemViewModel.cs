using System.Windows.Input;
using System.Windows.Media;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// One entry in the left navigation rail. Built from the module registry (for
    /// premium modules) or from the fixed core screens. <see cref="IsLocked"/> drives
    /// the 🔒 glyph and <see cref="IsSelected"/> the highlighted state.
    /// </summary>
    public sealed class NavItemViewModel : ObservableObject
    {
        private bool _isLocked;
        private bool _isSelected;

        public NavItemViewModel(string key, string title, Geometry icon, bool isModule, ICommand command)
        {
            Key = key;
            Title = title;
            Icon = icon;
            IsModule = isModule;
            Command = command;
        }

        public string Key { get; }
        public string Title { get; }
        public Geometry Icon { get; }
        public bool IsModule { get; }
        public ICommand Command { get; }

        /// <summary>True for a premium module the current license does not include.</summary>
        public bool IsLocked
        {
            get => _isLocked;
            set => Set(ref _isLocked, value);
        }

        /// <summary>True when this is the currently shown screen.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }
}
