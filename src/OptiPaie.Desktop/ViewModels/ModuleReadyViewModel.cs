using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// Shown when a premium module IS enabled by the license but its real screen has
    /// not been implemented yet. Prevents an empty page and confirms the module is
    /// active. When the real module screen is added later, the shell will map the
    /// module key to it and this placeholder disappears automatically.
    /// </summary>
    public sealed class ModuleReadyViewModel : ObservableObject
    {
        public ModuleReadyViewModel(string title)
        {
            Title = title;
            Subtitle = "Module activé";
        }

        public string Title { get; }
        public string Subtitle { get; }
    }
}
