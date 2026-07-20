namespace OptiPaie.Desktop.Mvvm
{
    /// <summary>Implemented by module view models that refresh their data when shown.</summary>
    public interface IActivable
    {
        void OnActivated();
    }
}
