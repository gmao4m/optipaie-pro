using System.Globalization;
using OptiPaie.Common.Constants;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;

namespace OptiPaie.Services
{
    /// <summary>Reads and writes UI/application preferences (the Settings table).</summary>
    public sealed class SettingsService : ISettingsService
    {
        private const string DefaultTheme = "The Bezier";
        private const decimal DefaultOvertimeMajoration = 0.5m;

        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public SettingsService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        public string GetLanguage()
        {
            return Get(SettingKeys.Language, AppConstants.DefaultLanguage);
        }

        public void SetLanguage(string code)
        {
            Set(SettingKeys.Language, code);
        }

        public string GetTheme()
        {
            return Get(SettingKeys.Theme, DefaultTheme);
        }

        public void SetTheme(string theme)
        {
            Set(SettingKeys.Theme, theme);
        }

        public long? GetDefaultCompanyId()
        {
            string raw = Get(SettingKeys.DefaultCompanyId, null);

            if (!string.IsNullOrWhiteSpace(raw) &&
                long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
            {
                return id;
            }

            return null;
        }

        public void SetDefaultCompanyId(long? companyId)
        {
            Set(SettingKeys.DefaultCompanyId,
                companyId.HasValue ? companyId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
        }

        public decimal GetOvertimeMajoration()
        {
            string raw = Get(SettingKeys.OvertimeMajoration, null);

            if (!string.IsNullOrWhiteSpace(raw) &&
                decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value))
            {
                return value;
            }

            return DefaultOvertimeMajoration;
        }

        public string Get(string key, string defaultValue = null)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                AppSetting setting = uow.AppSettings.Get(key);
                return setting == null || setting.SettingValue == null ? defaultValue : setting.SettingValue;
            }
        }

        public void Set(string key, string value)
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.AppSettings.Upsert(key, value);
            }
        }
    }
}
