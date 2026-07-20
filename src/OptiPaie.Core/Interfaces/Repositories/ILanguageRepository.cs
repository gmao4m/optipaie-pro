using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Read operations for supported <see cref="Language"/> metadata.</summary>
    public interface ILanguageRepository
    {
        /// <summary>Returns the enabled languages, in display order.</summary>
        IEnumerable<Language> GetAllEnabled();

        /// <summary>Returns the language with the given code, or null.</summary>
        Language GetByCode(string code);
    }
}
