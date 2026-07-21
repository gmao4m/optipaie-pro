using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OptiPaie.Core.Licensing;
using OptiPaie.Services.Licensing;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The free trial and the license gate. Proves the trial is 48 hours, one per
    /// machine, and that while it runs EVERY module is unlocked (the demo shows the whole
    /// product) — then locks again once the trial has expired with no license.
    /// </summary>
    [TestFixture]
    public sealed class TrialAndGateTests
    {
        private static TrialService NewTrial(InMemoryTrialStore store)
        {
            return new TrialService(store, new PassThroughCipher(), new NullTestLogger());
        }

        // ---------------------------------------------------------------- 48-hour trial

        [Test]
        public void StartTrial_GivesAbout48Hours()
        {
            var service = NewTrial(new InMemoryTrialStore());

            TrialInfo info = service.StartTrial();

            Assert.That(info.IsActive, Is.True);
            Assert.That(info.IsExpired, Is.False);
            Assert.That(info.HoursRemaining, Is.EqualTo(48), "the trial lasts 48 hours");
            Assert.That(info.ExpiresUtc.Value - info.StartedUtc.Value,
                Is.EqualTo(TimeSpan.FromHours(48)).Within(TimeSpan.FromSeconds(2)));
        }

        [Test]
        public void RemainingText_IsHumanFriendly()
        {
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            // 30 hours left → "1 j 6 h".
            var info = new TrialInfo(true, start, start.AddHours(48), start.AddHours(18));
            Assert.That(info.RemainingText, Is.EqualTo("1 j 6 h"));
            Assert.That(info.HoursRemaining, Is.EqualTo(30));
        }

        [Test]
        public void Trial_CannotBeRestartedOnceUsed()
        {
            var store = new InMemoryTrialStore();
            var service = NewTrial(store);

            service.StartTrial();
            // Simulate that the 48 hours have elapsed by rewriting the stored expiry.
            store.ForceExpired();

            TrialInfo reopened = NewTrial(store).GetStatus();
            Assert.That(reopened.IsExpired, Is.True, "an elapsed trial reports expired");

            TrialInfo restart = NewTrial(store).StartTrial();
            Assert.That(restart.IsActive, Is.False, "a trial can be started only once per machine");
            Assert.That(restart.IsExpired, Is.True);
        }

        [Test]
        public void GetStatus_BeforeStart_IsNotStarted()
        {
            TrialInfo info = NewTrial(new InMemoryTrialStore()).GetStatus();

            Assert.That(info.HasStarted, Is.False);
            Assert.That(info.IsActive, Is.False);
            Assert.That(info.IsExpired, Is.False);
        }

        // ---------------------------------------------------------------- gate: all modules in trial

        [Test]
        public void Gate_DuringTrial_UnlocksEveryModule()
        {
            var trial = NewTrial(new InMemoryTrialStore());
            trial.StartTrial();
            var gate = new LicenseGate(new FakeLicensing(LicenseSnapshot.NotActivated()), trial);

            Assert.That(gate.IsUsable, Is.True, "the app is usable during the trial");
            foreach (string module in new[]
            {
                ModuleKeys.Attendance, ModuleKeys.Leave, ModuleKeys.Loans, ModuleKeys.Contracts,
                ModuleKeys.Performance, ModuleKeys.Assets, ModuleKeys.Training, ModuleKeys.Ats,
                ModuleKeys.WorkCertificate
            })
            {
                Assert.That(gate.IsEnabled(module), Is.True, module + " must be unlocked in the demo");
            }
        }

        [Test]
        public void Gate_WithoutTrialOrLicense_LocksEveryModule()
        {
            var trial = NewTrial(new InMemoryTrialStore()); // never started
            var gate = new LicenseGate(new FakeLicensing(LicenseSnapshot.NotActivated()), trial);

            Assert.That(gate.IsUsable, Is.False);
            Assert.That(gate.IsEnabled(ModuleKeys.Leave), Is.False);
            Assert.That(gate.IsEnabled(ModuleKeys.Ats), Is.False);
        }

        [Test]
        public void Gate_AfterTrialExpired_LocksEveryModule()
        {
            var store = new InMemoryTrialStore();
            NewTrial(store).StartTrial();
            store.ForceExpired();

            var gate = new LicenseGate(new FakeLicensing(LicenseSnapshot.NotActivated()), NewTrial(store));

            Assert.That(gate.IsUsable, Is.False, "an expired trial no longer grants access");
            Assert.That(gate.IsEnabled(ModuleKeys.Training), Is.False);
        }

        // ---------------------------------------------------------------- test doubles

        private sealed class InMemoryTrialStore : ITrialStore
        {
            private string _blob;
            public string Load() => _blob;
            public void Save(string blob) => _blob = blob;
            public void Clear() => _blob = null;

            /// <summary>Rewrites the stored plaintext JSON so the expiry is in the past.</summary>
            public void ForceExpired()
            {
                if (string.IsNullOrEmpty(_blob)) return;
                DateTime past = DateTime.UtcNow.AddHours(-1).ToUniversalTime();
                string iso = past.ToString("o");
                // Set Expires and LastSeen into the past; keep Started earlier still.
                _blob =
                    "{\"StartedUtc\":\"" + DateTime.UtcNow.AddHours(-49).ToString("o") + "\"," +
                    "\"ExpiresUtc\":\"" + iso + "\"," +
                    "\"LastSeenUtc\":\"" + iso + "\"}";
            }
        }

        /// <summary>Identity cipher — the store keeps plaintext JSON for the test.</summary>
        private sealed class PassThroughCipher : ILocalCipher
        {
            public string Protect(string plainText) => plainText;
            public string Unprotect(string protectedBase64) => protectedBase64;
        }

        private sealed class FakeLicensing : ILicensingService
        {
            public FakeLicensing(LicenseSnapshot snapshot) { Current = snapshot; }
            public LicenseSnapshot Current { get; }
            public string DeviceId => "TEST-DEVICE";
            public event EventHandler Changed { add { } remove { } }
            public LicenseSnapshot Refresh() => Current;
            public Task<LicenseResult> ActivateAsync(string k, string c, string e, CancellationToken t) => throw new NotSupportedException();
            public Task<LicenseResult> SynchronizeAsync(CancellationToken t) => throw new NotSupportedException();
            public Task<LicenseResult> ActivateModuleAsync(string k, CancellationToken t) => throw new NotSupportedException();
            public void Deactivate() { }
        }

        private sealed class NullTestLogger : OptiPaie.Common.Logging.ILogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message) { }
            public void Error(string message, Exception exception) { }
        }
    }
}
