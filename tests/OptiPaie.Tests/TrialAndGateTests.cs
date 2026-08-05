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

        // ---------------------------------------------------------------- company scope (mono/multi)

        /// <summary>An Active, usable snapshot carrying a given company scope (null = Multi).</summary>
        private static LicenseSnapshot ScopedSnapshot(int? maxCompanies, LicenseStateKind state = LicenseStateKind.Active)
        {
            DateTime now = DateTime.UtcNow;
            return new LicenseSnapshot(
                state, "payroll", "PAY-TEST-0001", "Atlas Industrie", "a@atlas.dz", "TEST-DEVICE", "active",
                new[] { ModuleKeys.Attendance }, now, now, null, now.AddDays(30),
                LicenseType.Lifetime, "Atlas Industrie", maxCompanies);
        }

        [Test]
        public void Gate_MonoLicense_AllowsOnlyOneCompany()
        {
            var gate = new LicenseGate(new FakeLicensing(ScopedSnapshot(1)), NewTrial(new InMemoryTrialStore()));

            Assert.That(gate.MaxCompanies, Is.EqualTo(1));
            Assert.That(gate.CanAddCompany(0), Is.True, "the first company is always allowed");
            Assert.That(gate.CanAddCompany(1), Is.False, "a second company needs a Multi licence");
        }

        [Test]
        public void Gate_MultiLicense_AllowsUnlimitedCompanies()
        {
            // Multi is the explicit sentinel 0 (unlimited).
            var gate = new LicenseGate(new FakeLicensing(ScopedSnapshot(0)), NewTrial(new InMemoryTrialStore()));

            Assert.That(gate.MaxCompanies, Is.Null, "0 → unlimited (Multi)");
            Assert.That(gate.CanAddCompany(0), Is.True);
            Assert.That(gate.CanAddCompany(9), Is.True);
        }

        [Test]
        public void Gate_UsableLicenseWithoutScopeField_IsTreatedAsMono()
        {
            // Revenue-hole guard: a signed token that carries NO maxCompanies field
            // (payload.MaxCompanies == null) must be read as Mono for a USABLE license —
            // never as Multi. Otherwise any field-less token would grant Multi for free.
            var gate = new LicenseGate(new FakeLicensing(ScopedSnapshot(null)), NewTrial(new InMemoryTrialStore()));

            Assert.That(gate.MaxCompanies, Is.EqualTo(1), "absent/null scope = Mono, matching the backend");
            Assert.That(gate.CanAddCompany(0), Is.True, "the first company is allowed");
            Assert.That(gate.CanAddCompany(1), Is.False, "but a second is not — no free Multi");
        }

        [Test]
        public void Gate_Trial_IsTreatedAsMono()
        {
            var trial = NewTrial(new InMemoryTrialStore());
            trial.StartTrial();
            var gate = new LicenseGate(new FakeLicensing(LicenseSnapshot.NotActivated()), trial);

            Assert.That(gate.MaxCompanies, Is.EqualTo(1), "the trial allows a single company");
            Assert.That(gate.CanAddCompany(0), Is.True);
            Assert.That(gate.CanAddCompany(1), Is.False);
        }

        [Test]
        public void Gate_Unactivated_IsTreatedAsMono()
        {
            var gate = new LicenseGate(new FakeLicensing(LicenseSnapshot.NotActivated()), NewTrial(new InMemoryTrialStore()));

            Assert.That(gate.MaxCompanies, Is.EqualTo(1));
            Assert.That(gate.CanAddCompany(0), Is.True);
            Assert.That(gate.CanAddCompany(1), Is.False);
        }

        [Test]
        public void Gate_SuspendedMultiLicense_DoesNotGrantMulti()
        {
            // A real Multi (maxCompanies=0) license that is NOT usable (suspended) must not
            // unlock multi-company: the gate falls back to Mono until it is usable again.
            var gate = new LicenseGate(new FakeLicensing(ScopedSnapshot(0, LicenseStateKind.Suspended)),
                NewTrial(new InMemoryTrialStore()));

            Assert.That(gate.MaxCompanies, Is.EqualTo(1));
            Assert.That(gate.CanAddCompany(1), Is.False);
        }

        [Test]
        public void Snapshot_Duration_AnnualCountsDown_LifetimeIsPerpetual()
        {
            DateTime now = DateTime.UtcNow;
            var annual = new LicenseSnapshot(LicenseStateKind.Active, "payroll", "PAY", "Co", "e", "DEV",
                "active", new[] { "payroll" }, now, now, now.AddDays(30), now.AddDays(30),
                LicenseType.Annual, "Co", 1);
            var lifetime = new LicenseSnapshot(LicenseStateKind.Active, "payroll", "PAY", "Co", "e", "DEV",
                "active", new[] { "payroll" }, now, now, null, now.AddDays(30),
                LicenseType.Lifetime, "Co", null);

            Assert.That(annual.DaysRemaining, Is.GreaterThan(0), "an annual/dated licence counts down to expiry");
            Assert.That(lifetime.DaysRemaining, Is.Null, "a lifetime licence never expires");
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
