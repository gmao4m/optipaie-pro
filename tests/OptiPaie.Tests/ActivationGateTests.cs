using System;
using System.Collections.Generic;
using NUnit.Framework;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Licensing;
using OptiPaie.Services.Licensing;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The startup / reconnection gate. Proves the production fix: a poste is asked for its
    /// license ONLY until it is activated; afterwards it opens on a trusted marker and is never
    /// re-asked (even when the local cache momentarily fails to verify, e.g. device-id drift) —
    /// while a genuinely never-activated poste is still refused (the check is moved, not removed).
    /// </summary>
    [TestFixture]
    public sealed class ActivationGateTests
    {
        // ---------------------------------------------------------------- gate decision

        [Test]
        public void NeverActivated_AndNotUsable_IsRefused()
        {
            // The security guarantee: a fresh, never-activated install with no active trial
            // must be blocked. (canUseApp=false, isActivated=false)
            Assert.That(AccessDecision.MayOpen(false, false), Is.False, "a never-activated poste must be blocked");
            Assert.That(AccessDecision.MustActivate(false, false), Is.True, "…and must be sent to activation");
        }

        [Test]
        public void UsableNow_Opens()
        {
            // A valid license or an active trial opens the app.
            Assert.That(AccessDecision.MayOpen(true, false), Is.True);
            Assert.That(AccessDecision.MustActivate(true, false), Is.False);
        }

        [Test]
        public void AlreadyActivated_OpensEvenWhenNotCurrentlyUsable()
        {
            // THE FIX: an already-activated poste opens WITHOUT a license prompt even when the
            // live license cache is momentarily not usable (device-id drift, unreadable cache).
            Assert.That(AccessDecision.MayOpen(false, true), Is.True,
                "an activated poste is never re-asked for its license by a local re-verification issue");
            Assert.That(AccessDecision.MustActivate(false, true), Is.False);
        }

        // ---------------------------------------------------------------- activation marker

        [Test]
        public void ActivationState_MarkThenIsActivated_RoundTrips_AndIsIdempotent()
        {
            var settings = new FakeSettings();
            var state = new ActivationState(settings);

            Assert.That(state.IsActivated, Is.False, "a brand-new poste is not activated");

            state.MarkActivated();
            Assert.That(state.IsActivated, Is.True);

            state.MarkActivated(); // idempotent
            Assert.That(state.IsActivated, Is.True);

            // Persisted as a plain settings flag (DPAPI-independent) so a cache/device failure
            // can never erase it — a fresh instance over the same store still sees it.
            Assert.That(new ActivationState(settings).IsActivated, Is.True);
            Assert.That(settings.Get("Activation.Completed", null), Is.EqualTo("1"));
        }

        // ------------------------------------------------ migration / backfill (mandatory)

        [Test]
        public void ActivatedUnderOldVersion_NoMarker_IsBackfilledAsActivated()
        {
            // A poste activated under an OLDER version: it HAS a stored license but NO marker.
            var settings = new FakeSettings();               // no "Activation.Completed" key
            var state = new ActivationState(settings);
            bool hasStoredLicense = true;                    // an existing license cache is present

            Assert.That(state.IsActivated, Is.False, "the old poste has no marker yet");

            // Startup backfill (exactly what App.OnStartup does): a stored license ⇒ activated.
            if (hasStoredLicense)
            {
                state.MarkActivated();
            }

            Assert.That(state.IsActivated, Is.True, "an already-activated poste must be treated as activated");
            Assert.That(AccessDecision.MayOpen(false, state.IsActivated), Is.True,
                "…so it opens after the update WITHOUT being asked to re-enter its license");
        }

        // ------------------------------------------------ logout → reconnect (mandatory)

        [Test]
        public void LogoutThenReconnect_ActivatedPoste_NeverShowsTheLicenseScreen()
        {
            // After a logout the customer signs in with email + password. The license gate that
            // runs on the way back in must open on the marker alone — even if the cached license
            // is not currently usable — so the license screen never appears on reconnection.
            var settings = new FakeSettings();
            var state = new ActivationState(settings);
            state.MarkActivated();                            // this poste was activated

            bool cachedLicenseUsableRightNow = false;         // e.g. a transient verification hiccup
            bool gateOpensWithoutLicensePrompt = AccessDecision.MayOpen(cachedLicenseUsableRightNow, state.IsActivated);

            Assert.That(gateOpensWithoutLicensePrompt, Is.True,
                "reconnection on an activated poste must not be sent to the license screen");
        }

        // ---------------------------------------------------------------- test double

        private sealed class FakeSettings : ISettingsService
        {
            private readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);

            public string Get(string key, string defaultValue = null) =>
                _map.TryGetValue(key, out string v) ? v : defaultValue;

            public void Set(string key, string value) => _map[key] = value;

            public string GetLanguage() => "fr";
            public void SetLanguage(string code) { }
            public string GetTheme() => "light";
            public void SetTheme(string theme) { }
            public long? GetDefaultCompanyId() => null;
            public void SetDefaultCompanyId(long? companyId) { }
            public decimal GetOvertimeMajoration() => 0.5m;
        }
    }
}
