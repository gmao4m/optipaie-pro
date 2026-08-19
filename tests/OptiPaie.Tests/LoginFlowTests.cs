using System;
using System.IO;
using NUnit.Framework;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Interfaces.Services;
using OptiPaie.Core.Licensing;
using OptiPaie.Core.Primitives;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Services;
using OptiPaie.Services.Auth;
using OptiPaie.Services.Licensing;

namespace OptiPaie.Tests
{
    /// <summary>
    /// The unified login flow: ONE screen, ONE identifier that is auto-detected as the owner
    /// email or a local username, converging on a single "open the app" path. Also proves the
    /// v1.26 production crash mechanism (ShutdownMode) with a WPF-free model.
    /// </summary>
    [TestFixture]
    public sealed class LoginFlowTests
    {
        private string _directory;
        private IUnitOfWorkFactory _uowf;
        private ISettingsService _settings;
        private IUserService _users;
        private ICustomerAccountService _owner;
        private LoginCoordinator _coordinator;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "optipaie-login-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_directory, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uowf = new UnitOfWorkFactory(factory);
            _settings = new SettingsService(_uowf);
            _users = new UserService(_uowf, _settings);
            _owner = new CustomerAccountService(_settings, new PassThroughCipher(), new SilentLogger());
            _coordinator = new LoginCoordinator(_owner, _users);
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }

        // -- unified auth: local username -------------------------------------

        [Test]
        public void Login_LocalUsername_CorrectPassword_SignsInAsLocalUser()
        {
            _users.Create("chef", "Chef Prod", "pass1234", UserRole.Manager, "Production");

            LoginOutcome outcome = _coordinator.Login("chef", "pass1234");

            Assert.That(outcome.Success, Is.True);
            Assert.That(outcome.Principal, Is.EqualTo(LoginPrincipal.LocalUser));
            Assert.That(outcome.User, Is.Not.Null);
            Assert.That(outcome.User.Username, Is.EqualTo("chef"));
        }

        // -- unified auth: owner email ----------------------------------------

        [Test]
        public void Login_OwnerEmail_CorrectPassword_SignsInAsOwner()
        {
            _owner.Register("boss@corp.dz", "Corp", "owner-pass");
            _owner.SignOut();

            LoginOutcome outcome = _coordinator.Login("BOSS@corp.dz", "owner-pass"); // case-insensitive

            Assert.That(outcome.Success, Is.True);
            Assert.That(outcome.Principal, Is.EqualTo(LoginPrincipal.Owner));
            Assert.That(_owner.IsSignedIn, Is.True, "the owner session is opened");
        }

        [Test]
        public void Login_OwnerEmail_IsAnIndependentRescuePath_WhateverTheLocalUsers()
        {
            _owner.Register("boss@corp.dz", "Corp", "owner-pass");
            _owner.SignOut();

            // A local admin exists and the gate is ON — the owner email is still its own path,
            // so a client who forgot every local password can always get back in.
            _users.Create("admin", "Admin", "adminpass", UserRole.Admin, null);
            _users.SetLoginEnabled(true);

            LoginOutcome outcome = _coordinator.Login("boss@corp.dz", "owner-pass");
            Assert.That(outcome.Success, Is.True);
            Assert.That(outcome.Principal, Is.EqualTo(LoginPrincipal.Owner));
            Assert.That(_owner.IsSignedIn, Is.True);
        }

        // -- precise failures --------------------------------------------------

        [Test]
        public void Login_DisabledLocalUser_CorrectPassword_ReportsDisabled_NotBadPassword()
        {
            _users.Create("admin", "Admin", "adminpass", UserRole.Admin, null); // keep an active admin
            long id = _users.Create("old", "Old User", "pass1234", UserRole.Manager, null).Value;
            User u = _users.Get(id);
            u.IsActive = false;
            _users.Update(u);

            LoginOutcome outcome = _coordinator.Login("old", "pass1234"); // RIGHT password, disabled account
            Assert.That(outcome.Success, Is.False);
            Assert.That(outcome.Failure, Is.EqualTo(LoginFailure.Disabled));
        }

        [Test]
        public void Login_WrongPassword_ReportsBadCredentials()
        {
            _users.Create("chef", "Chef", "pass1234", UserRole.Manager, null);
            Assert.That(_coordinator.Login("chef", "nope").Failure, Is.EqualTo(LoginFailure.BadCredentials));
        }

        [Test]
        public void Login_MissingFields_ReportedDistinctly()
        {
            Assert.That(_coordinator.Login("", "x").Failure, Is.EqualTo(LoginFailure.MissingIdentifier));
            Assert.That(_coordinator.Login("someone", "").Failure, Is.EqualTo(LoginFailure.MissingPassword));
        }

        [Test]
        public void Login_ExceptionOnTheLoginPath_IsContained_ReportedAsTechnical_NeverThrows()
        {
            // An unexpected failure in the auth backend must be turned into a shown/logged reason,
            // never an unhandled exception that could close the app.
            var coordinator = new LoginCoordinator(_owner, new ThrowingUserService());

            LoginOutcome outcome = null;
            Assert.DoesNotThrow(() => outcome = coordinator.Login("someone", "whatever"));
            Assert.That(outcome, Is.Not.Null);
            Assert.That(outcome.Success, Is.False);
            Assert.That(outcome.Failure, Is.EqualTo(LoginFailure.Technical));
        }

        // -- PROOF #1: the ShutdownMode crash mechanism (WPF-free model) -------
        //
        // The real WPF ShutdownMode termination cannot be exercised headlessly (no STA
        // Application). WindowLifecycleModel is a faithful model of the exact mechanism —
        // open-window count + shutdown mode — so the bug and its fix are provable.

        [TestCase(AppShutdownMode.OnLastWindowClose, true)]    // WPF default (current) → process dies
        [TestCase(AppShutdownMode.OnExplicitShutdown, false)]  // the fix → process survives
        public void StartupGate_LoginWindowCloseTerminatesProcess_OnlyUnderLastWindowClose(
            AppShutdownMode mode, bool expectTerminated)
        {
            // At the gate the login window is the ONLY window (MainWindow is created only
            // AFTER a successful login).
            var life = new WindowLifecycleModel(mode);
            life.OpenWindow();   // unified login dialog shown (sole window)
            life.CloseWindow();  // correct credentials → dialog closes
            Assert.That(life.Terminated, Is.EqualTo(expectTerminated));
        }

        [Test]
        public void LocalLogin_CorrectPassword_ReachesMainWindow_ProcessStaysAlive()
        {
            // Uses the SAME shutdown policy the real WPF App applies at startup.
            var life = new WindowLifecycleModel(AppShellPolicy.ShutdownModeDuringGate);
            life.OpenWindow();   // login shown (sole window)
            life.CloseWindow();  // correct local password → closes
            Assert.That(life.Terminated, Is.False, "a correct login must NEVER close the app");

            life.OpenWindow();   // MainWindow now opens
            Assert.That(life.Terminated, Is.False);
            Assert.That(life.OpenWindows, Is.EqualTo(1), "the main window is open and the process is alive");
        }

        [Test]
        public void TwoSuccessiveSignOutLoginCycles_KeepTheProcessAlive()
        {
            var life = new WindowLifecycleModel(AppShellPolicy.ShutdownModeDuringGate);
            life.OpenWindow(); // MainWindow after first startup

            for (int cycle = 0; cycle < 2; cycle++)
            {
                life.CloseWindow();  // signout closes the shell
                Assert.That(life.Terminated, Is.False, "signout must return to login, not exit");
                life.OpenWindow();   // unified login
                life.CloseWindow();  // re-login success
                Assert.That(life.Terminated, Is.False);
                life.OpenWindow();   // fresh MainWindow
            }

            Assert.That(life.Terminated, Is.False);
            Assert.That(life.OpenWindows, Is.EqualTo(1));
        }

        [Test]
        public void App_UsesTheSurvivingShutdownMode()
        {
            Assert.That(AppShellPolicy.ShutdownModeDuringGate, Is.EqualTo(AppShutdownMode.OnExplicitShutdown));
        }

        // -- recovery escapes on the unified login screen (fix of the 1.27.0 regressions) ----

        [Test]
        public void LoginMessageMap_EachFailure_MapsToADistinctMessageKey()
        {
            Assert.That(LoginMessageMap.KeyFor(LoginFailure.MissingIdentifier, false), Is.EqualTo("Login_Err_NeedUsername"));
            Assert.That(LoginMessageMap.KeyFor(LoginFailure.MissingPassword, false), Is.EqualTo("Login_Err_NeedPassword"));
            Assert.That(LoginMessageMap.KeyFor(LoginFailure.Disabled, true), Is.EqualTo("Login_Err_Disabled"));
            Assert.That(LoginMessageMap.KeyFor(LoginFailure.Technical, false), Is.EqualTo("Login_Err_Technical"));

            // A generic bad-credentials is split for clarity — unknown identifier vs wrong password.
            Assert.That(LoginMessageMap.KeyFor(LoginFailure.BadCredentials, identifierExists: false), Is.EqualTo("Login_Err_UnknownIdentifier"));
            Assert.That(LoginMessageMap.KeyFor(LoginFailure.BadCredentials, identifierExists: true), Is.EqualTo("Login_Err_WrongPassword"));

            // Every distinct case yields a distinct, non-empty key (each error shows its own message).
            var keys = new[]
            {
                LoginMessageMap.KeyFor(LoginFailure.MissingIdentifier, false),
                LoginMessageMap.KeyFor(LoginFailure.MissingPassword, false),
                LoginMessageMap.KeyFor(LoginFailure.BadCredentials, false),
                LoginMessageMap.KeyFor(LoginFailure.BadCredentials, true),
                LoginMessageMap.KeyFor(LoginFailure.Disabled, false),
                LoginMessageMap.KeyFor(LoginFailure.Technical, false),
            };
            Assert.That(keys, Is.Unique);
            Assert.That(keys, Has.None.Empty);
        }

        [Test]
        public void LoginScreen_AlwaysOffers_LicenseEscape_AndUpdateCheck()
        {
            // Both affordances are PERMANENT (never conditioned on a failed attempt): the
            // license-key escape to activation (owner recovery) and the update check. The login
            // view/screen bind to these, so a blocked user is never stranded.
            Assert.That(LoginScreenAffordances.LicenseKeyEscapeAlwaysVisible, Is.True, "path to activation must exist & be visible");
            Assert.That(LoginScreenAffordances.UpdateCheckAlwaysVisible, Is.True, "update check must be present on the login screen");
        }

        [Test]
        public void Owner_ForgotPassword_NoLocalUser_RegainsAccess_ViaReactivation_NotLockedOut()
        {
            // Owner exists, is signed out, and has forgotten the password; no local user exists.
            _owner.Register("boss@corp.dz", "Corp", "old-password");
            _owner.SignOut();
            Assert.That(_owner.SignIn("boss@corp.dz", "forgotten-guess"), Is.False, "the old password is lost");
            Assert.That(_users.HasActiveAdmin(), Is.False, "no local user to fall back to");

            // The login screen's « الدخول بمفتاح الترخيص » escape opens activation, where
            // re-activating re-creates the account with a NEW password and signs it in.
            _owner.Register("boss@corp.dz", "Corp", "new-password"); // = what activation does on success

            Assert.That(_owner.IsSignedIn, Is.True, "re-activation signs the owner back in — NOT locked out");
            _owner.SignOut();
            Assert.That(_owner.SignIn("boss@corp.dz", "new-password"), Is.True, "the new password works");
            Assert.That(_owner.SignIn("boss@corp.dz", "old-password"), Is.False, "the old one no longer does");
        }

        // -- test doubles ------------------------------------------------------

        private sealed class PassThroughCipher : ILocalCipher
        {
            public string Protect(string plainText) => plainText;
            public string Unprotect(string protectedBase64) => protectedBase64;
        }

        private sealed class SilentLogger : ILogger
        {
            public void Info(string message) { }
            public void Warn(string message) { }
            public void Error(string message) { }
            public void Error(string message, Exception exception) { }
        }

        /// <summary>An IUserService whose Authenticate throws — to prove the coordinator contains it.</summary>
        private sealed class ThrowingUserService : IUserService
        {
            public Result<User> Authenticate(string username, string password) =>
                throw new InvalidOperationException("injected failure on the login path");

            public Result<long> Create(string username, string fullName, string password, UserRole role, string department) => throw new NotSupportedException();
            public Result Update(User user) => throw new NotSupportedException();
            public Result ChangePassword(long userId, string newPassword) => throw new NotSupportedException();
            public Result Delete(long userId) => throw new NotSupportedException();
            public User Get(long id) => throw new NotSupportedException();
            public System.Collections.Generic.IReadOnlyList<User> GetAll() => throw new NotSupportedException();
            public int ActiveUserCount() => 0;
            public bool HasActiveAdmin() => false;
            public bool IsLoginRequired() => false;
            public bool IsLoginEnabled() => false;
            public void SetLoginEnabled(bool enabled) { }
        }
    }
}
