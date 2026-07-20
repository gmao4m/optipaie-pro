using OptiPaie.Core.Enums;

namespace OptiPaie.Core.Entities
{
    /// <summary>
    /// Metadata for a supported UI language (direction and preferred font).
    /// <para>
    /// UI string translations live in .resx resources, and data translations live
    /// in the bilingual columns of the entities; this table only holds what the
    /// runtime needs to lay out and render each language. The code is the PK.
    /// </para>
    /// </summary>
    public sealed class Language
    {
        /// <summary>ISO language code ("fr", "ar"). Primary key.</summary>
        public string Code { get; set; }

        /// <summary>Native display name ("Français", "العربية").</summary>
        public string NameNative { get; set; }

        /// <summary>Text/layout direction.</summary>
        public LanguageDirection Direction { get; set; }

        /// <summary>Preferred font family for screen and print.</summary>
        public string FontFamily { get; set; }

        /// <summary>Whether the language is selectable.</summary>
        public bool IsEnabled { get; set; }

        /// <summary>Display order in the language selector.</summary>
        public int DisplayOrder { get; set; }
    }
}
