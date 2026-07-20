using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OptiPaie.Desktop.Mvvm
{
    /// <summary>Minimal INotifyPropertyChanged base for view models.</summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            Raise(name);
            return true;
        }

        protected void Raise([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
