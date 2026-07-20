using System;
using System.Globalization;
using OptiPaie.Common.Constants;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Dtos;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services
{
    /// <summary>
    /// Assembles the configurable payroll legal values and the rounding policy, and
    /// builds the immutable <see cref="LegalSnapshot"/> the engine consumes. This is
    /// pure configuration reading — it performs NO payroll calculation.
    /// </summary>
    public sealed class ConfigurationService : IConfigurationService
    {
        // Fallbacks matching the seeded 2026 values, used only if a parameter is absent.
        private const decimal DefaultCnasEmployeeRate = 0.09m;
        private const decimal DefaultCnasEmployerRate = 0.26m;
        private const decimal DefaultSnmg = 24000m;
        private const int DefaultRoundingScale = 2;

        private readonly IUnitOfWorkFactory _unitOfWorkFactory;

        public ConfigurationService(IUnitOfWorkFactory unitOfWorkFactory)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
        }

        public decimal GetCnasEmployeeRate()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return ReadDecimal(uow, LegalParameterKeys.CnasEmployeeRate, DefaultCnasEmployeeRate);
            }
        }

        public decimal GetCnasEmployerRate()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return ReadDecimal(uow, LegalParameterKeys.CnasEmployerRate, DefaultCnasEmployerRate);
            }
        }

        public decimal GetSnmg()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return ReadDecimal(uow, LegalParameterKeys.Snmg, DefaultSnmg);
            }
        }

        public RoundingPolicy GetRoundingPolicy()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                return ReadRoundingPolicy(uow);
            }
        }

        public LegalSnapshot GetLegalSnapshot()
        {
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                decimal cnasEmployee = ReadDecimal(uow, LegalParameterKeys.CnasEmployeeRate, DefaultCnasEmployeeRate);
                decimal cnasEmployer = ReadDecimal(uow, LegalParameterKeys.CnasEmployerRate, DefaultCnasEmployerRate);
                decimal snmg = ReadDecimal(uow, LegalParameterKeys.Snmg, DefaultSnmg);
                RoundingPolicy rounding = ReadRoundingPolicy(uow);

                return new LegalSnapshot(cnasEmployee, cnasEmployer, snmg, rounding);
            }
        }

        private static decimal ReadDecimal(IUnitOfWork uow, string key, decimal fallback)
        {
            LegalParameter parameter = uow.LegalParameters.GetActiveByKey(key);

            if (parameter == null || string.IsNullOrWhiteSpace(parameter.ParamValue))
            {
                return fallback;
            }

            if (decimal.TryParse(parameter.ParamValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value))
            {
                return value;
            }

            return fallback;
        }

        private static RoundingPolicy ReadRoundingPolicy(IUnitOfWork uow)
        {
            int scale = DefaultRoundingScale;
            AppSetting setting = uow.AppSettings.Get(SettingKeys.RoundingScale);

            if (setting != null &&
                int.TryParse(setting.SettingValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                scale = parsed;
            }

            if (scale < 0)
            {
                scale = 0;
            }
            else if (scale > 4)
            {
                scale = 4;
            }

            return new RoundingPolicy(scale, MidpointRounding.AwayFromZero);
        }
    }
}
