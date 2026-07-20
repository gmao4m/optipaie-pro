using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OptiPaie.Desktop.Mvvm;

namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>
    /// The premium (upsell) page shown when the user opens a module their license does
    /// not include. Presentation only — it sells the module and offers a support
    /// contact. When the module is later enabled, the shell navigates away from this
    /// page automatically (no reinstall).
    /// </summary>
    public sealed class PremiumModuleViewModel : ObservableObject
    {
        private readonly PremiumModuleContent _content;

        public PremiumModuleViewModel(PremiumModuleContent content)
        {
            _content = content;
            Icon = Application.Current != null
                ? Application.Current.TryFindResource(content.IconKey) as Geometry
                : null;
            Features = content.Features ?? new string[0];
            ContactSupportCommand = new RelayCommand(ContactSupport);
        }

        public string Title => _content.Title;
        public string Description => _content.Description;
        public Geometry Icon { get; }
        public IReadOnlyList<string> Features { get; }

        public string LockTitle => "Ce module n'est pas inclus dans votre licence actuelle";

        public string LockMessage =>
            "Pour activer ce module, veuillez contacter notre équipe de support.";

        public ICommand ContactSupportCommand { get; }

        private void ContactSupport()
        {
            // Placeholder action for now (per spec). Real routing comes later.
            MessageBox.Show(
                "Pour activer « " + _content.Title + " », contactez notre équipe de support :\r\n\r\n" +
                "Email : support@optipaie.dz",
                "Contacter le support",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
