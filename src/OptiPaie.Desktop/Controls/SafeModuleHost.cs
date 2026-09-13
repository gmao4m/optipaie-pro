using System;
using System.Windows;
using System.Windows.Controls;
using OptiPaie.Desktop.Common;

namespace OptiPaie.Desktop.Controls
{
    /// <summary>
    /// Hosts the active module view. It instantiates the module's implicit DataTemplate EAGERLY and
    /// SYNCHRONOUSLY (inside a try/catch) so a view that fails to construct — e.g. an unresolved
    /// <c>{StaticResource}</c> key — is caught HERE and replaced by a visible placeholder, instead of
    /// throwing on WPF's deferred render pass (which escapes to the dispatcher handler and leaves the
    /// whole window broken — the 1.38.0 startup outage). A single broken screen must never make the
    /// application unusable: the sidebar stays live and the user can open another module.
    /// </summary>
    public sealed class SafeModuleHost : ContentControl
    {
        private bool _guarding;

        protected override void OnContentChanged(object oldContent, object newContent)
        {
            base.OnContentChanged(oldContent, newContent);

            // A FrameworkElement is our own placeholder (or an already-built view) — nothing to guard.
            if (_guarding || newContent == null || newContent is FrameworkElement)
            {
                return;
            }

            try
            {
                // Force the module's implicit DataTemplate to instantiate now, surfacing any XAML /
                // resource error synchronously rather than on the later (uncatchable) render pass.
                if (TryFindResource(new DataTemplateKey(newContent.GetType())) is DataTemplate template)
                {
                    template.LoadContent();
                }
            }
            catch (Exception ex)
            {
                CrashLog.Fatal("Module render (" + newContent.GetType().Name + ")", ex);
                _guarding = true;
                try { Content = BuildPlaceholder(ex); }
                finally { _guarding = false; }
            }
        }

        private static FrameworkElement BuildPlaceholder(Exception ex)
        {
            var message = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 540,
                FontSize = 14,
                Text = "تعذّر عرض هذه الوحدة. اختر وحدة أخرى من القائمة؛ التفاصيل التقنية سُجّلت." +
                       "\n\nCe module n'a pas pu s'afficher. Choisissez un autre module dans le menu ; " +
                       "le détail technique a été enregistré." +
                       "\n\n(" + (ex == null ? "?" : ex.GetType().Name) + ")"
            };

            return new Border { Padding = new Thickness(40), Child = message };
        }
    }
}
