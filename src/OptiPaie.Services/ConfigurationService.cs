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
        private readonly OptiPaie.Core.Security.IAccessContext _access;
        private OptiPaie.Core.Auditing.IAuditSink _audit = OptiPaie.Core.Auditing.NullAuditSink.Instance;

        public ConfigurationService(
            IUnitOfWorkFactory unitOfWorkFactory,
            OptiPaie.Core.Security.IAccessContext access = null)
        {
            _unitOfWorkFactory = Guard.AgainstNull(unitOfWorkFactory, nameof(unitOfWorkFactory));
            _access = access;
        }

        /// <summary>Audit sink for setting changes (wired by the composition root, like the other services).</summary>
        public OptiPaie.Core.Auditing.IAuditSink Audit
        {
            get => _audit;
            set => _audit = value ?? OptiPaie.Core.Auditing.NullAuditSink.Instance;
        }

        public bool GetLegalDeductionsEnabled()
        {
            try
            {
                using (IUnitOfWork uow = _unitOfWorkFactory.Create())
                {
                    AppSetting setting = uow.AppSettings.Get(SettingKeys.LegalDeductionsEnabled);
                    return LegalDeductionsSetting.IsEnabled(setting?.SettingValue);
                }
            }
            catch (Exception)
            {
                // FAIL-SAFE: a read failure must never silently disable the legal deductions.
                return true;
            }
        }

        public Result SetLegalDeductionsEnabled(bool enabled)
        {
            // Fails closed: no access context, or a role without ManageSettings (administrators only).
            OptiPaie.Core.Security.AccessPolicy.Require(_access, OptiPaie.Core.Security.Permission.ManageSettings);

            bool before = GetLegalDeductionsEnabled();
            using (IUnitOfWork uow = _unitOfWorkFactory.Create())
            {
                uow.AppSettings.Upsert(SettingKeys.LegalDeductionsEnabled,
                    enabled ? LegalDeductionsSetting.On : LegalDeductionsSetting.Off);
            }

            if (before != enabled)
            {
                // Who and when are stamped by the audit sink (actor provider + timestamp).
                _audit.Record(
                    "AppSetting", 0, OptiPaie.Core.Auditing.AuditAction.Updated,
                    "Calcul IRG et CNAS : " + OnOff(before) + " → " + OnOff(enabled)
                        + " / حساب IRG و CNAS : " + OnOff(before) + " ← " + OnOff(enabled),
                    OnOff(before), OnOff(enabled));
            }

            return Result.Ok();
        }

        private static string OnOff(bool value) => value ? "ON" : "OFF";

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
