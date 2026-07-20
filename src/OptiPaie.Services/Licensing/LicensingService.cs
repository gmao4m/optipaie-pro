using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using OptiPaie.Common.Logging;
using OptiPaie.Common.Validation;
using OptiPaie.Core.Licensing;

namespace OptiPaie.Services.Licensing
{
    /// <summary>
    /// Orchestrates licensing: offline verification at startup, online activation and
    /// synchronization, offline grace and an anti clock-rollback guard. Holds the
    /// current snapshot in memory and raises <see cref="Changed"/> when it moves.
    /// <para>
    /// Never performs network I/O in its constructor — construction only reads and
    /// verifies the local cache, so the app starts instantly and works offline.
    /// </para>
    /// </summary>
    public sealed class LicensingService : ILicensingService
    {
        private readonly ILicenseBackend _backend;
        private readonly ISignedLicenseVerifier _verifier;
        private readonly IDeviceIdentity _deviceIdentity;
        private readonly ILicenseStore _store;
        private readonly ILocalCipher _cipher;
        private readonly LicensingOptions _options;
        private readonly ILogger _logger;

        private readonly object _gate = new object();
        private readonly string _deviceId;
        private LicenseSnapshot _current;

        public LicensingService(
            ILicenseBackend backend,
            ISignedLicenseVerifier verifier,
            IDeviceIdentity deviceIdentity,
            ILicenseStore store,
            ILocalCipher cipher,
            LicensingOptions options,
            ILogger logger)
        {
            _backend = Guard.AgainstNull(backend, nameof(backend));
            _verifier = Guard.AgainstNull(verifier, nameof(verifier));
            _deviceIdentity = Guard.AgainstNull(deviceIdentity, nameof(deviceIdentity));
            _store = Guard.AgainstNull(store, nameof(store));
            _cipher = Guard.AgainstNull(cipher, nameof(cipher));
            _options = Guard.AgainstNull(options, nameof(options));
            _logger = Guard.AgainstNull(logger, nameof(logger));

            _deviceId = SafeDeviceId();
            _current = BuildFromStore();
        }

        /// <summary>Loads the stored license and decrypts its protected fields (token, key).</summary>
        private StoredLicense LoadDecrypted()
        {
            StoredLicense stored = _store.Load();
            if (stored == null)
            {
                return null;
            }

            // Fields were encrypted at rest; fall back to the raw value for legacy
            // (plaintext) caches so upgrades keep working.
            stored.SignedToken = _cipher.Unprotect(stored.SignedToken) ?? stored.SignedToken;
            stored.LicenseKey = _cipher.Unprotect(stored.LicenseKey) ?? stored.LicenseKey;
            return stored;
        }

        public event EventHandler Changed;

        public LicenseSnapshot Current
        {
            get { lock (_gate) { return _current; } }
        }

        public string DeviceId => _deviceId;

        public LicenseSnapshot Refresh()
        {
            LicenseSnapshot snapshot = BuildFromStore();
            SetCurrent(snapshot);
            return snapshot;
        }

        public async Task<LicenseResult> ActivateAsync(
            string licenseKey, string companyName, string email, CancellationToken cancellationToken)
        {
            if (!_options.IsConfigured)
            {
                return LicenseResult.Create(LicenseResultKind.NotConfigured,
                    "Le service de licence n'est pas configuré.", Current);
            }

            var request = new ActivationRequest
            {
                ProductKey = _options.ProductKey,
                LicenseKey = Normalize(licenseKey),
                DeviceId = _deviceId,
                CompanyName = (companyName ?? string.Empty).Trim(),
                Email = (email ?? string.Empty).Trim(),
                AppVersion = _options.AppVersion
            };

            BackendLicenseResponse response;
            try
            {
                response = await _backend.ActivateAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn("Activation could not reach the server: " + ex.Message);
                return LicenseResult.Create(LicenseResultKind.Offline,
                    "Aucune connexion Internet. L'activation nécessite une connexion.", Current);
            }

            if (!response.Success)
            {
                return MapError(response);
            }

            return PersistAndBuild(response.Token, "activation");
        }

        public async Task<LicenseResult> SynchronizeAsync(CancellationToken cancellationToken)
        {
            if (!_options.IsConfigured)
            {
                return LicenseResult.Create(LicenseResultKind.NotConfigured, string.Empty, Current);
            }

            StoredLicense stored = LoadDecrypted();
            if (stored == null || string.IsNullOrWhiteSpace(stored.LicenseKey))
            {
                return LicenseResult.Create(LicenseResultKind.NotActivated, string.Empty, Current);
            }

            var request = new ValidationRequest
            {
                ProductKey = _options.ProductKey,
                LicenseKey = stored.LicenseKey,
                DeviceId = _deviceId,
                AppVersion = _options.AppVersion
            };

            BackendLicenseResponse response;
            try
            {
                response = await _backend.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Offline is normal and must never interrupt the user: keep the last
                // valid local license (still within its grace window).
                _logger.Info("License sync skipped (offline): " + ex.Message);
                return LicenseResult.Create(LicenseResultKind.Offline, string.Empty, Refresh());
            }

            if (!response.Success)
            {
                // Definitive server error (e.g. invalid_key). Be conservative: report
                // it but keep the last valid local license so a transient backend issue
                // can't lock a paying customer out. Suspension/revocation come back as a
                // successful 200 with the new status and are handled below.
                _logger.Warn("License validation returned an error: " + (response.ErrorCode ?? "unknown"));
                LicenseResult mapped = MapError(response);
                return LicenseResult.Create(mapped.Kind, mapped.Message, Refresh());
            }

            return PersistAndBuild(response.Token, "synchronization");
        }

        public async Task<LicenseResult> ActivateModuleAsync(string activationKey, CancellationToken cancellationToken)
        {
            if (!_options.IsConfigured)
            {
                return LicenseResult.Create(LicenseResultKind.NotConfigured,
                    "Le service de licence n'est pas configuré.", Current);
            }

            StoredLicense stored = LoadDecrypted();
            if (stored == null || string.IsNullOrWhiteSpace(stored.LicenseKey))
            {
                return LicenseResult.Create(LicenseResultKind.NotActivated,
                    "Activez d'abord une licence avant d'ajouter un module.", Current);
            }

            var request = new ModuleActivationRequest
            {
                ProductKey = _options.ProductKey,
                LicenseKey = stored.LicenseKey,
                DeviceId = _deviceId,
                ActivationKey = (activationKey ?? string.Empty).Trim().ToUpperInvariant(),
                AppVersion = _options.AppVersion
            };

            BackendLicenseResponse response;
            try
            {
                response = await _backend.ActivateModuleAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn("Module activation could not reach the server: " + ex.Message);
                return LicenseResult.Create(LicenseResultKind.Offline,
                    "Aucune connexion Internet. L'activation d'un module nécessite une connexion.", Current);
            }

            if (!response.Success)
            {
                return MapError(response);
            }

            // Verifies signature + product + device, persists the encrypted cache, and
            // refreshes (raising Changed → the UI unlocks the module immediately).
            return PersistAndBuild(response.Token, "module activation");
        }

        public void Deactivate()
        {
            try
            {
                _store.Clear();
                _logger.Info("License deactivated locally.");
            }
            catch (Exception ex)
            {
                _logger.Warn("Could not clear the local license: " + ex.Message);
            }

            Refresh();
        }

        // -- internals ---------------------------------------------------------

        private LicenseResult PersistAndBuild(string token, string context)
        {
            if (!_verifier.TryVerify(token, out SignedLicensePayload payload))
            {
                _logger.Error("Signed license failed verification during " + context + ".");
                return LicenseResult.Create(LicenseResultKind.Error,
                    "Réponse de licence invalide.", Current);
            }

            if (!IsForThisApp(payload))
            {
                _logger.Error("Signed license did not match this product/device during " + context + ".");
                return LicenseResult.Create(LicenseResultKind.Error,
                    "Licence non conforme à cet appareil.", Current);
            }

            Persist(token, payload);
            LicenseSnapshot snapshot = Refresh();

            if (snapshot.IsUsable)
            {
                return LicenseResult.Ok(snapshot);
            }

            // Sync/activation succeeded but the license is not usable (suspended, etc.).
            return LicenseResult.Create(KindForState(snapshot.State), MessageForState(snapshot.State), snapshot);
        }

        private void Persist(string token, SignedLicensePayload payload)
        {
            StoredLicense existing = _store.Load();
            DateTime now = DateTime.UtcNow;
            DateTime lastSeen = MaxUtc(ParseIso(existing != null ? existing.LastSeenUtc : null), now);

            var stored = new StoredLicense
            {
                ProductKey = payload.Product,
                // Sensitive fields are encrypted at rest (DPAPI); non-secret metadata
                // is left readable for quick display (it is also inside the signed token).
                LicenseKey = _cipher.Protect(payload.LicenseKey) ?? payload.LicenseKey,
                CompanyName = payload.CompanyName,
                Email = payload.Email,
                DeviceId = payload.DeviceId,
                Status = payload.Status,
                SignedToken = _cipher.Protect(token) ?? token,
                ActivatedAtUtc = existing != null && !string.IsNullOrEmpty(existing.ActivatedAtUtc)
                    ? existing.ActivatedAtUtc
                    : ToIso(now),
                LastValidationUtc = ToIso(now),
                ExpiresAtUtc = ToIso(payload.ExpiresAt),
                GraceUntilUtc = ToIso(payload.GraceUntil),
                LastSeenUtc = ToIso(lastSeen)
            };

            _store.Save(stored, payload.Modules ?? Array.Empty<string>());
        }

        private LicenseSnapshot BuildFromStore()
        {
            StoredLicense stored;
            try
            {
                stored = LoadDecrypted();
            }
            catch (Exception ex)
            {
                _logger.Error("Could not read the local license.", ex);
                return LicenseSnapshot.NotActivated();
            }

            if (stored == null || string.IsNullOrWhiteSpace(stored.LicenseKey) ||
                string.IsNullOrWhiteSpace(stored.SignedToken))
            {
                return LicenseSnapshot.NotActivated();
            }

            if (!_verifier.TryVerify(stored.SignedToken, out SignedLicensePayload payload) || !IsForThisApp(payload))
            {
                _logger.Warn("Stored license is invalid for this machine/product.");
                return InvalidSnapshot(stored);
            }

            // Anti clock-rollback: never let the effective time go backwards from the
            // highest value we have ever observed, so expiry/grace can't be cheated.
            DateTime now = DateTime.UtcNow;
            DateTime effectiveNow = MaxUtc(ParseIso(stored.LastSeenUtc), now);
            try
            {
                _store.UpdateLastSeen(ToIso(effectiveNow));
            }
            catch (Exception ex)
            {
                _logger.Warn("Could not persist last-seen time: " + ex.Message);
            }

            LicenseStateKind state = ComputeState(payload, effectiveNow);

            return new LicenseSnapshot(
                state,
                payload.Product,
                payload.LicenseKey,
                payload.CompanyName,
                payload.Email,
                payload.DeviceId,
                payload.Status,
                payload.Modules,
                ParseIso(stored.ActivatedAtUtc),
                ParseIso(stored.LastValidationUtc),
                payload.ExpiresAt,
                payload.GraceUntil,
                LicenseTypes.Parse(payload.Type),
                payload.CustomerName);
        }

        private static LicenseStateKind ComputeState(SignedLicensePayload payload, DateTime now)
        {
            string status = (payload.Status ?? string.Empty).Trim().ToLowerInvariant();

            if (status == "suspended")
            {
                return LicenseStateKind.Suspended;
            }

            if (status == "revoked")
            {
                return LicenseStateKind.Revoked;
            }

            if (status != "active")
            {
                return LicenseStateKind.Invalid;
            }

            if (payload.ExpiresAt.HasValue && now > payload.ExpiresAt.Value)
            {
                return LicenseStateKind.Expired;
            }

            if (payload.GraceUntil.HasValue && now > payload.GraceUntil.Value)
            {
                return LicenseStateKind.GraceExpired;
            }

            return LicenseStateKind.Active;
        }

        private bool IsForThisApp(SignedLicensePayload payload)
        {
            return payload != null
                && string.Equals(payload.Product, _options.ProductKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(payload.DeviceId, _deviceId, StringComparison.Ordinal);
        }

        private LicenseSnapshot InvalidSnapshot(StoredLicense stored)
        {
            return new LicenseSnapshot(
                LicenseStateKind.Invalid,
                _options.ProductKey,
                stored.LicenseKey,
                stored.CompanyName,
                stored.Email,
                _deviceId,
                stored.Status,
                null, null, null, null, null);
        }

        private void SetCurrent(LicenseSnapshot snapshot)
        {
            lock (_gate)
            {
                _current = snapshot;
            }

            EventHandler handler = Changed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private string SafeDeviceId()
        {
            try
            {
                return _deviceIdentity.GetDeviceId();
            }
            catch (Exception ex)
            {
                _logger.Error("Could not compute the device id.", ex);
                return "UNKNOWN-DEVICE";
            }
        }

        private static LicenseResult MapError(BackendLicenseResponse response)
        {
            string code = (response.ErrorCode ?? string.Empty).Trim().ToLowerInvariant();
            LicenseResultKind kind;
            string message;

            switch (code)
            {
                case "invalid_key":
                    kind = LicenseResultKind.InvalidKey;
                    message = "Clé de licence invalide.";
                    break;
                case "device_in_use":
                    kind = LicenseResultKind.DeviceInUse;
                    message = "Licence déjà activée sur un autre appareil.";
                    break;
                case "device_mismatch":
                    kind = LicenseResultKind.DeviceMismatch;
                    message = "Licence liée à un autre appareil.";
                    break;
                case "suspended":
                    kind = LicenseResultKind.Suspended;
                    message = "Licence suspendue. Contactez le fournisseur.";
                    break;
                case "revoked":
                    kind = LicenseResultKind.Revoked;
                    message = "Licence révoquée. Contactez le fournisseur.";
                    break;
                case "wrong_product":
                    kind = LicenseResultKind.WrongProduct;
                    message = "Cette clé n'appartient pas à ce logiciel.";
                    break;
                case "unknown_product":
                    kind = LicenseResultKind.UnknownProduct;
                    message = "Produit non reconnu par le serveur.";
                    break;
                case "key_invalid":
                    kind = LicenseResultKind.KeyInvalid;
                    message = "Clé d'activation invalide.";
                    break;
                case "key_used":
                    kind = LicenseResultKind.KeyUsed;
                    message = "Cette clé d'activation a déjà été utilisée.";
                    break;
                case "key_revoked":
                    kind = LicenseResultKind.KeyRevoked;
                    message = "Cette clé d'activation a été révoquée.";
                    break;
                case "key_expired":
                    kind = LicenseResultKind.KeyExpired;
                    message = "Cette clé d'activation a expiré.";
                    break;
                case "key_wrong_license":
                    kind = LicenseResultKind.KeyWrongLicense;
                    message = "Cette clé n'appartient pas à votre licence.";
                    break;
                case "module_already_active":
                    kind = LicenseResultKind.ModuleAlreadyActive;
                    message = "Ce module est déjà activé.";
                    break;
                default:
                    kind = LicenseResultKind.Error;
                    message = string.IsNullOrWhiteSpace(response.ErrorMessage)
                        ? "Une erreur est survenue lors de la vérification de la licence."
                        : response.ErrorMessage;
                    break;
            }

            return LicenseResult.Create(kind, message, null);
        }

        private static LicenseResultKind KindForState(LicenseStateKind state)
        {
            switch (state)
            {
                case LicenseStateKind.Suspended: return LicenseResultKind.Suspended;
                case LicenseStateKind.Revoked: return LicenseResultKind.Revoked;
                default: return LicenseResultKind.Error;
            }
        }

        private static string MessageForState(LicenseStateKind state)
        {
            switch (state)
            {
                case LicenseStateKind.Suspended: return "Licence suspendue. Contactez le fournisseur.";
                case LicenseStateKind.Revoked: return "Licence révoquée. Contactez le fournisseur.";
                case LicenseStateKind.Expired: return "Licence expirée.";
                case LicenseStateKind.GraceExpired: return "Veuillez vous connecter à Internet pour valider la licence.";
                default: return "Licence non utilisable.";
            }
        }

        private static string Normalize(string key)
        {
            return (key ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static DateTime MaxUtc(DateTime? a, DateTime b)
        {
            return a.HasValue && a.Value > b ? a.Value : b;
        }

        private static string ToIso(DateTime value)
        {
            return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        private static string ToIso(DateTime? value)
        {
            return value.HasValue ? ToIso(value.Value) : null;
        }

        private static DateTime? ParseIso(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            // ISO-8601 "o" values are written in UTC (trailing Z); RoundtripKind parses
            // them back as UTC. (RoundtripKind must not be combined with AdjustToUniversal.)
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTime parsed))
            {
                return parsed.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                    : parsed.ToUniversalTime();
            }

            return null;
        }
    }
}
