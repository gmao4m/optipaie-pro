using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace OptiPaie.Desktop.Localization
{
    /// <summary>
    /// XAML markup extension for localized text: <c>{loc:Loc Common_Save}</c>. It binds the
    /// target property to <see cref="TranslationSource"/>'s string indexer, so the text is
    /// resolved for the active language and updates live when the language switches.
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class LocExtension : MarkupExtension
    {
        public LocExtension()
        {
        }

        public LocExtension(string key)
        {
            Key = key;
        }

        /// <summary>The resource key to resolve (e.g. "Common_Save").</summary>
        [ConstructorArgument("key")]
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding("[" + Key + "]")
            {
                Source = TranslationSource.Instance,
                Mode = BindingMode.OneWay
            };

            return binding.ProvideValue(serviceProvider);
        }
    }
}
