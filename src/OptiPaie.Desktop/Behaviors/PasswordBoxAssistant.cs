using System.Windows;
using System.Windows.Controls;

namespace OptiPaie.Desktop.Behaviors
{
    /// <summary>
    /// Lets a <see cref="PasswordBox"/> two-way bind its (masked) password to a view-model string —
    /// WPF forbids binding <see cref="PasswordBox.Password"/> directly (for security). Use it so a
    /// password is never entered in a plain, shoulder-surfable TextBox:
    /// <code>&lt;PasswordBox beh:PasswordBoxAssistant.BoundPassword="{Binding NewPassword, Mode=TwoWay}" /&gt;</code>
    /// </summary>
    public static class PasswordBoxAssistant
    {
        public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
            "BoundPassword", typeof(string), typeof(PasswordBoxAssistant),
            new FrameworkPropertyMetadata(string.Empty, OnBoundPasswordChanged));

        private static readonly DependencyProperty UpdatingProperty = DependencyProperty.RegisterAttached(
            "Updating", typeof(bool), typeof(PasswordBoxAssistant), new PropertyMetadata(false));

        public static string GetBoundPassword(DependencyObject d) => (string)d.GetValue(BoundPasswordProperty);
        public static void SetBoundPassword(DependencyObject d, string value) => d.SetValue(BoundPasswordProperty, value);

        private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is PasswordBox box)) return;

            box.PasswordChanged -= HandlePasswordChanged;
            if (!(bool)box.GetValue(UpdatingProperty))
            {
                box.Password = e.NewValue as string ?? string.Empty;
            }
            box.PasswordChanged += HandlePasswordChanged;
        }

        private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
        {
            var box = (PasswordBox)sender;
            box.SetValue(UpdatingProperty, true);
            SetBoundPassword(box, box.Password);
            box.SetValue(UpdatingProperty, false);
        }
    }
}
