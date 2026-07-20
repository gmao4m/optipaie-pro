using System.Collections.Generic;
using OptiPaie.Core.Entities;

namespace OptiPaie.Core.Interfaces.Repositories
{
    /// <summary>Persistence operations for <see cref="AppSetting"/> key/value preferences.</summary>
    public interface IAppSettingRepository
    {
        /// <summary>Returns the setting for a key, or null.</summary>
        AppSetting Get(string key);

        /// <summary>Returns all settings.</summary>
        IEnumerable<AppSetting> GetAll();

        /// <summary>Inserts or updates the value for a key.</summary>
        void Upsert(string key, string value);
    }
}
