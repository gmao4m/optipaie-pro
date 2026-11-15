using System;
using OptiPaie.Common.Logging;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Auditing;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Licensing;
using OptiPaie.Core.Primitives;

namespace OptiPaie.Services.Startup
{
    /// <summary>
    /// The first-launch-after-upgrade path. 300 live installs predate the permission system; with
    /// fail-closed authorization and no migration, every one of them would be at risk of being
    /// locked out of its own data. This service closes that gap: it establishes the OWNER on first
    /// launch — adopting an existing account in place (password preserved) or walking the customer
    /// through creating one — and it NEVER touches, re-keys, or migrates the business data. If
    /// establishing the owner cannot complete, the caller leaves the database exactly as it was and
    /// refuses to open with a restricted view (see <c>App.OnStartup</c>).
    /// <para>
    /// All state lives in the key/value settings store (no schema migration): the "owner established"
    /// marker, and the account itself (via <see cref="ICustomerAccountService"/>). Fully offline.
    /// </para>
    /// </summary>
    public sealed class OwnerSetupService
    {
        private const string KeyOwnerEstablished = "Account.OwnerEstablished"; // "1" once setup has completed

        private readonly ISettingsService _settings;
        private readonly ICustomerAccountService _account;
        private readonly ICompanyService _companies;
        private readonly ILogger _logger;

        public OwnerSetupService(
            ISettingsService settings,
            ICustomerAccountService account,
            ICompanyService companies,
            ILogger logger)
        {
            _settings = Guard.AgainstNull(settings, nameof(settings));
            _account = Guard.AgainstNull(account, nameof(account));
            _companies = Guard.AgainstNull(companies, nameof(companies));
            _logger = Guard.AgainstNull(logger, nameof(logger));
        }

        /// <summary>Audit sink for owner-setup events (best-effort; wired by composition).</summary>
        public IAuditSink Audit { get; set; } = NullAuditSink.Instance;

        /// <summary>True once the one-time owner setup has completed on this installation.</summary>
        public bool IsOwnerEstablished =>
            string.Equals(Get(KeyOwnerEstablished), "1", StringComparison.Ordinal);

        /// <summary>True when the database already holds real business data (at least one company).</summary>
        public bool HasBusinessData
        {
            get
            {
                try { return _companies.GetAll().Count > 0; }
                catch (Exception ex)
                {
                    // A read failure must never be read as "no data" — that could wrongly route a
                    // customer with data into the create-owner path. Treat an unreadable DB as
                    // "has data" so the safe (non-creating) branch is taken.
                    _logger.Warn("Owner setup: could not probe business data, assuming present: " + ex.Message);
                    return true;
                }
            }
        }

        /// <summary>Classifies this installation for the first-launch path. Reads only; never writes.</summary>
        public OwnerSetupAction DecideAction()
        {
            return OwnerSetupPolicy.Decide(IsOwnerEstablished, _account.HasAccount, HasBusinessData);
        }

        /// <summary>
        /// Adopts a pre-existing customer account, in place, as the OWNER: it marks setup complete so
        /// the path never runs again. The account — and its password — is preserved exactly; the
        /// customer still signs in with the password they already have. No business data is touched.
        /// </summary>
        public void AdoptExistingAccount()
        {
            if (!_account.HasAccount)
            {
                // Defensive: never mark established without an actual owner to adopt.
                _logger.Warn("Owner setup: AdoptExistingAccount called with no account present — ignored.");
                return;
            }

            MarkEstablished();
            Audit.Record("OwnerSetup", 0, AuditAction.StatusChanged,
                "Compte propriétaire existant adopté lors de la mise à jour (mot de passe conservé).");
            _logger.Info("Owner setup: existing account adopted as owner (password preserved).");
        }

        /// <summary>
        /// Creates the OWNER account from the setup wizard, opens its session, issues the one-time
        /// recovery code (returned to be shown ONCE), and marks setup complete. On any validation
        /// failure NOTHING is written: no account, no marker — so a cancelled/failed setup leaves the
        /// installation exactly as it was. The business data is never touched by this method.
        /// </summary>
        /// <returns>Ok(recoveryCode) on success; Fail(bilingual message) otherwise.</returns>
        public Result<string> CreateOwner(string email, string company, string password)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return Result.Fail<string>(
                    "L'adresse e-mail est requise.\nعنوان البريد الإلكتروني مطلوب.", "Owner_EmailRequired");
            }

            if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            {
                return Result.Fail<string>(
                    "Le mot de passe doit contenir au moins " + MinPasswordLength + " caractères.\n" +
                    "يجب أن تتكوّن كلمة السرّ من " + MinPasswordLength + " محارف على الأقلّ.",
                    "Owner_PasswordTooShort");
            }

            try
            {
                _account.Register(email.Trim(), company ?? string.Empty, password); // creates + signs in
                string code = _account.IssueRecoveryCode();                          // one-time recovery code
                MarkEstablished();

                Audit.Record("OwnerSetup", 0, AuditAction.Created,
                    "Compte propriétaire créé lors de la première ouverture après mise à jour.");
                _logger.Info("Owner setup: owner account created and recovery code issued.");
                return Result.Ok(code);
            }
            catch (Exception ex)
            {
                // Leave the installation untouched on failure: do NOT mark established.
                _logger.Error("Owner setup: creating the owner account failed.", ex);
                return Result.Fail<string>(
                    "La création du compte propriétaire a échoué. La base de données n'a pas été modifiée.\n" +
                    "فشل إنشاء حساب المالك. لم يتمّ تعديل قاعدة البيانات.",
                    "Owner_CreateFailed");
            }
        }

        /// <summary>
        /// Backfills the one-time recovery code for an established owner that has none yet (an account
        /// created before the recovery feature existed). Returns the new code to show ONCE, or null
        /// when nothing was issued (no account, or a code already exists). Idempotent and offline.
        /// </summary>
        public string BackfillRecoveryCodeIfMissing()
        {
            if (!_account.HasAccount || _account.HasRecoveryCode)
            {
                return null;
            }

            string code = _account.IssueRecoveryCode();
            if (!string.IsNullOrEmpty(code))
            {
                Audit.Record("OwnerSetup", 0, AuditAction.StatusChanged,
                    "Code de récupération généré pour un compte propriétaire existant (rattrapage).");
                _logger.Info("Owner setup: recovery code backfilled for an existing account.");
            }
            return code;
        }

        /// <summary>Marks the one-time owner setup as complete (idempotent).</summary>
        public void MarkEstablished()
        {
            Set(KeyOwnerEstablished, "1");
        }

        // The owner password obeys the same minimum length as every local user password.
        private static int MinPasswordLength => OptiPaie.Services.UserService.MinPasswordLength;

        private string Get(string key)
        {
            try { return _settings.Get(key, null); }
            catch (Exception ex) { _logger.Warn("Owner setup: could not read '" + key + "': " + ex.Message); return null; }
        }

        private void Set(string key, string value)
        {
            try { _settings.Set(key, value ?? string.Empty); }
            catch (Exception ex) { _logger.Warn("Owner setup: could not write '" + key + "': " + ex.Message); }
        }
    }
}
