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
                { "LicensingService", "Supabase/network license backend + device identity" },
                { "CustomerAccountService", "DPAPI-wrapped owner account over settings (sign-in flow)" },
                { "OwnerSetupService", "depends on the DPAPI-wrapped owner account (ILocalCipher); its only Result method is the first-run owner bootstrap, exempt by attribute (chicken-and-egg: it creates the first owner before any role exists)" },
                { "TrialService", "trial cipher + store (device-bound)" },
                { "AtsDrtDocumentService", "renders onto pre-printed form assets on disk (not a DB write service)" },
                { "AuditService", "audit sink (void Record; optional string actor ctor param); no Result methods" },
                { "BatchPayrollService", "takes a delegate predicate; no Result methods; transitively guarded via the guarded PayrollService.Generate it calls" },
                { "UpdateService", "auto-update release channel (network); no DB-write Result methods" },
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

        [Test]
        public void EveryResultReturningServiceMethod_IsGuarded_OrExplicitlyExempt()
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

                MethodInfo[] methods = svc.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName && ReturnsResult(m.ReturnType))
                    .OrderBy(m => m.Name).ToArray();

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
                    object[] args = ctor.GetParameters().Select(p => Resolve(p.ParameterType)).ToArray();
                    return ctor.Invoke(args);
                }
            }

            throw new InvalidOperationException("cannot resolve " + t.FullName);
        }
    }
}
