using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using OptiPaie.Common.Configuration;
using OptiPaie.Common.Logging;
using OptiPaie.Core.Interfaces;
using OptiPaie.Core.Interfaces.Engine;
using OptiPaie.Core.Primitives;
using OptiPaie.Core.Security;
using OptiPaie.Data.Backup;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.PayrollEngine;
using OptiPaie.Services;
using OptiPaie.Services.Validation;

namespace OptiPaie.Tests
{
    /// <summary>
    /// Commit 2 — the rot-proof write-guard test. At RUNTIME it enumerates every *Service class in the
    /// domain assembly, constructs it with an access context that holds NO permissions, and asserts
    /// that every public method returning Result/Result&lt;T&gt; is REJECTED (AccessDeniedException) —
    /// unless the method is explicitly marked <see cref="WriteGuardExemptAttribute"/> with a reason.
    /// It fails automatically the day someone adds a new unguarded mutation, or an exemption with no
    /// reason, or a new service the harness cannot classify. The full enumeration is printed.
    /// </summary>
    [TestFixture]
    public sealed class WriteGuardReflectionTests
    {
        private string _dir;
        private SqliteConnectionFactory _factory;
        private UnitOfWorkFactory _uow;
        private AppConfiguration _config;
        private readonly Dictionary<Type, object> _cache = new Dictionary<Type, object>();

        // Services that genuinely cannot be unit-constructed here (network / device / external I/O),
        // each with the reason. They are reported as EXCLUDED, not silently skipped.
        private static readonly Dictionary<string, string> Unbuildable =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // GENUINELY out of scope (not laziness): these three are licensing/trial/update
                // INFRA. Their state changes gate on the license, the trial, or the device — NEVER on
                // a user role — so there is no role-guarded mutation for THIS test to verify. They
                // also need network/crypto/device seams to construct; building them would add zero
                // write-guard coverage. (AtsDrtDocumentService and AuditService were formerly excluded
                // too, but they ARE buildable and role-neutral — they are now constructed below, G.4.)
                { "LicensingService", "licensing infra: activation/sync gate on the license + device identity, never on a user role; needs a network backend + Ed25519 verifier seam to build" },
                { "TrialService", "trial infra: device-bound trial state, gated on the trial/license, never on a user role; needs an ITrialStore seam to build" },
                { "UpdateService", "auto-update infra: a network release channel, no role-guarded DB write; needs an IReleaseChannel seam to build" },
            };

        /// <summary>
        /// Write entry points that DO NOT return <see cref="Result"/> and so would escape a
        /// return-type-only scope. Scoping by SERVICE ROLE, these are verified explicitly: each must
        /// reject a no-permission caller (throw <see cref="AccessDeniedException"/>). A new non-Result
        /// write entry point must be registered here (or guard-tested some other way) — this is the
        /// gap that previously let <c>BatchPayrollService.Run</c> slip through.
        /// </summary>
        private static readonly Dictionary<string, string[]> NonResultWriteEntryPoints =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "BatchPayrollService", new[] { "Run" } }, // persists a payslip per employee
            };

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-wgr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            _factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = _factory.CreateOpenConnection()) new MigrationRunner(c).Run();
            _uow = new UnitOfWorkFactory(_factory);
            _config = new AppConfiguration(_dir, "fr");
        }

        [TearDown]
        public void TearDown()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private sealed class NoPermAccess : IAccessContext
        {
            public bool IsEnforced => true;
            public OptiPaie.Core.Enums.UserRole CurrentRole => OptiPaie.Core.Enums.UserRole.ReadOnly;
            public bool IsOwner => false;
            public long? CurrentUserId => null;
        }

        private sealed class PassthroughCipher : OptiPaie.Core.Licensing.ILocalCipher
        {
            public string Protect(string plaintext) => plaintext;
            public string Unprotect(string ciphertext) => ciphertext;
        }

        [Test]
        public void EveryServiceWriteEntryPoint_IsGuarded_OrExplicitlyExempt()
        {
            Assembly asm = typeof(EmployeeService).Assembly;
            Type[] services = asm.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic && t.Name.EndsWith("Service", StringComparison.Ordinal))
                .OrderBy(t => t.Name)
                .ToArray();

            var report = new List<string>();
            var failures = new List<string>();
            var exemptions = new List<string>();
            int checkedMethods = 0;

            foreach (Type svc in services)
            {
                if (Unbuildable.ContainsKey(svc.Name))
                {
                    report.Add("EXCLUDED  " + svc.Name + "  — " + Unbuildable[svc.Name]);
                    continue;
                }

                object instance;
                try
                {
                    instance = Resolve(svc);
                }
                catch (Exception ex)
                {
                    failures.Add("Could not construct " + svc.Name + " (classify it: guard+register, or add to Unbuildable with a reason). " + Root(ex).Message);
                    continue;
                }

                // Scope by SERVICE ROLE: a write entry point is in scope whatever it returns. The
                // return-type scan catches every Result-returning write; explicitly-registered
                // non-Result write entry points (e.g. BatchPayrollService.Run) are added alongside.
                NonResultWriteEntryPoints.TryGetValue(svc.Name, out string[] extraWriteNames);
                var extraWrites = new HashSet<string>(extraWriteNames ?? new string[0], StringComparer.Ordinal);

                MethodInfo[] methods = svc.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName && (ReturnsResult(m.ReturnType) || extraWrites.Contains(m.Name)))
                    .OrderBy(m => m.Name).ToArray();

                // A registered non-Result write entry point that no longer exists (renamed/removed)
                // must fail loudly, not silently stop being checked.
                foreach (string name in extraWrites)
                {
                    if (!methods.Any(m => m.Name == name))
                    {
                        failures.Add("Registered non-Result write entry point " + svc.Name + "." + name +
                            " no longer exists — update NonResultWriteEntryPoints.");
                    }
                }

                foreach (MethodInfo m in methods)
                {
                    var exempt = m.GetCustomAttribute<WriteGuardExemptAttribute>();
                    if (exempt != null)
                    {
                        if (string.IsNullOrWhiteSpace(exempt.Reason))
                        {
                            failures.Add(svc.Name + "." + m.Name + " is [WriteGuardExempt] with NO reason.");
                        }
                        exemptions.Add(svc.Name + "." + m.Name + "  — " + exempt.Reason);
                        report.Add("EXEMPT    " + svc.Name + "." + m.Name + "  — " + exempt.Reason);
                        continue;
                    }

                    checkedMethods++;
                    object[] args = m.GetParameters()
                        .Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
                        .ToArray();

                    bool denied = false;
                    try
                    {
                        m.Invoke(instance, args);
                    }
                    catch (TargetInvocationException tie) when (tie.InnerException is AccessDeniedException)
                    {
                        denied = true;
                    }
                    catch (Exception ex) when (Root(ex) is AccessDeniedException)
                    {
                        denied = true;
                    }
                    catch
                    {
                        denied = false; // any other outcome = NOT guarded (it ran past the check)
                    }

                    report.Add((denied ? "GUARDED   " : "UNGUARDED " ) + svc.Name + "." + m.Name);
                    if (!denied)
                    {
                        failures.Add("UNGUARDED mutation: " + svc.Name + "." + m.Name +
                            " did not reject a no-permission caller. Add a permission check, or mark it [WriteGuardExempt(\"reason\")] if it truly writes nothing.");
                    }
                }
            }

            TestContext.WriteLine("=== Write-guard enumeration (" + checkedMethods + " guarded methods checked, " +
                exemptions.Count + " exempt) ===");
            foreach (string line in report) TestContext.WriteLine(line);

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        private static bool ReturnsResult(Type rt)
        {
            if (rt == typeof(Result)) return true;
            return rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Result<>);
        }

        private static Exception Root(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        // ---- tiny recursive resolver ----
        private object Resolve(Type t)
        {
            if (_cache.TryGetValue(t, out object cached)) return cached;

            object made = Make(t);
            _cache[t] = made;
            return made;
        }

        private object Make(Type t)
        {
            if (t == typeof(IAccessContext)) return new NoPermAccess();
            if (t == typeof(Func<string, bool>)) return (Func<string, bool>)(_ => true); // module-enabled predicate
            if (t == typeof(OptiPaie.Core.Licensing.ILocalCipher)) return new PassthroughCipher(); // test seam: no DPAPI needed
            if (t == typeof(OptiPaie.Core.Interfaces.Repositories.IUnitOfWorkFactory)) return _uow;
            if (t == typeof(AppConfiguration)) return _config;
            if (t == typeof(ILogger)) return NullLogger.Instance;
            if (t == typeof(IPayrollEngine)) return new PayrollCalculationEngine();
            if (t == typeof(IDatabaseBackupProvider)) return new SqliteBackupProvider(_factory, _config);
            if (t == typeof(OptiPaie.Common.Validation.IValidator<OptiPaie.Core.Entities.Employee>)) return new EmployeeValidator();
            if (t == typeof(OptiPaie.Common.Validation.IValidator<OptiPaie.Core.Entities.Company>)) return new CompanyValidator();
            if (t == typeof(OptiPaie.Common.Validation.IValidator<OptiPaie.Core.Entities.PayrollElement>)) return new PayrollElementValidator();

            // Map a service interface (IFooService) to its concrete (FooService) in the Services assembly.
            if (t.IsInterface && t.Name.StartsWith("I", StringComparison.Ordinal) && t.Name.EndsWith("Service", StringComparison.Ordinal))
            {
                Type concrete = typeof(EmployeeService).Assembly.GetTypes()
                    .FirstOrDefault(x => x.IsClass && !x.IsAbstract && x.Name == t.Name.Substring(1));
                if (concrete != null) return Resolve(concrete);
            }

            // Build a concrete class by its greediest public constructor.
            if (t.IsClass && !t.IsAbstract)
            {
                ConstructorInfo ctor = t.GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                if (ctor != null)
                {
                    object[] args = ctor.GetParameters().Select(p =>
                    {
                        // Honour optional parameters (e.g. an optional "string actor"): if the type
                        // cannot be resolved but the parameter has a default, use the default.
                        try { return Resolve(p.ParameterType); }
                        catch when (p.IsOptional) { return p.DefaultValue; }
                    }).ToArray();
                    return ctor.Invoke(args);
                }
            }

            throw new InvalidOperationException("cannot resolve " + t.FullName);
        }
    }
}
