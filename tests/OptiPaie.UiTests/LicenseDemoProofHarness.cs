using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NUnit.Framework;
using OptiPaie.Common.Constants;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Enums;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Core.Security;
using OptiPaie.Desktop;
using OptiPaie.Desktop.Common;
using OptiPaie.Desktop.Composition;
using OptiPaie.Desktop.Localization;
using OptiPaie.Desktop.ViewModels;
using OptiPaie.Desktop.Views;
using OptiPaie.Services;
using OptiPaie.Services.Licensing;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// [Explicit] proof harness for 1.45.0. Writes to docs/licence-demo/:
    ///   — the demo removal on a COPY of the real database (DEMO_PROOF_DB, else the live file is COPIED —
    ///     never opened): identification, the only-company refusal, then a removal through the REAL
    ///     Settings button flow after a customer company is added, with every number;
    ///   — screenshots: Settings (FR/AR) with "تحديث الترخيص" and the demo block, the preview dialog
    ///     (FR/AR, normal and refused), and the REAL licence-updated message box (captured with PrintWindow).
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA), Explicit]
    public sealed class LicenseDemoProofHarness
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private string _out;

        [OneTimeSetUp]
        public void Setup()
        {
            if (Application.Current == null)
            {
                var app = new App();
                typeof(App).GetMethod("InitializeComponent", BindingFlags.Public | BindingFlags.Instance)?.Invoke(app, null);
            }
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            _out = Path.Combine(FindRepoRoot(), "docs", "licence-demo");
            Directory.CreateDirectory(_out);
        }

        [Test]
        public void Demo_Removal_On_A_Copy_Of_The_Real_Database()
        {
            string source = Environment.GetEnvironmentVariable("DEMO_PROOF_DB");
            if (string.IsNullOrWhiteSpace(source))
                source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OptiPaie DZ", AppConstants.DatabaseFileName);
            Assert.That(File.Exists(source), Is.True, "source database not found");

            string dir = Path.Combine(Path.GetTempPath(), "optipaie-demo-proof-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string db = Path.Combine(dir, AppConstants.DatabaseFileName);
            File.Copy(source, db); // the source is only READ (copied); everything below runs on the copy
            // Screenshots must not show this PC's licence key / device id: drop the licence cache ON THE COPY.
            using (var c = Open(db)) { Exec(c, "DELETE FROM LicenseState;"); Exec(c, "DELETE FROM LicenseModules;"); }
            var r = new StringBuilder();
            r.AppendLine("DEMO REMOVAL — on a COPY of the real database (never the live file)");
            r.AppendLine("source copied : " + Path.GetFileName(source) + "  (" + new FileInfo(source).Length.ToString("N0", Fr) + " bytes)");
            r.AppendLine();

            try
            {
                AppServices services = CompositionRoot.Build(new OptiPaie.Common.Configuration.AppConfiguration(dir, "fr"));
                TranslationSource.Instance.Attach(services.Localization);
                ThemeManager.Apply(false);
                services.CompanyContext.Reload();

                r.AppendLine("schema version after upgrade : " + Scalar(db, "SELECT MAX(Version) FROM SchemaMigrations"));
                r.AppendLine("companies in the copy (demo identifiers shown — the user had edited its CNAS number):");
                foreach (string line in Rows(db, "SELECT '  #' || Id || ' NIF=' || IFNULL(Nif,'') || ' NIS=' || IFNULL(Nis,'') || ' RC=' || IFNULL(Rc,'') || ' AI=' || IFNULL(ArticleImposition,'') || " +
                                                  "' CNAS=' || IFNULL(CnasEmployerNumber,'') || ' updated=' || IFNULL(UpdatedAtUtc,'') FROM Companies WHERE IsDeleted = 0"))
                    r.AppendLine(line);
                r.AppendLine("  departments of the demo: " + string.Join(", ", Rows(db, "SELECT Name FROM Departments WHERE CompanyId = 3 ORDER BY Id")));
                foreach (string line in Rows(db, "SELECT Id || ' | ' || NameFr || ' | IsDemo=' || IsDemo || ' | IsDeleted=' || IsDeleted || ' | employees=' || " +
                                                  "(SELECT COUNT(*) FROM Employees e WHERE e.CompanyId = c.Id) FROM Companies c ORDER BY Id"))
                    r.AppendLine("  " + line);

                // ---------------------------------------------------------- 1. identification
                DemoCompanyInfo demo = services.DemoData.FindDemoCompany();
                Assert.That(demo, Is.Not.Null, "the trial demo company must be recognised in this database");
                r.AppendLine();
                r.AppendLine("[1] IDENTIFICATION → company #" + demo.Id + " « " + demo.Name + " » recognised by: " + demo.MatchedBy +
                             (demo.MatchedBy == "fingerprint" ? " (seeded before the flag existed: the 4 seeded legal identifiers NIF/NIS/RC/AI + the exact 20-name roster + departments among the seeded ones and the default « Générale »)" : ""));

                // ---------------------------------------------------------- 2. only company → refused
                DemoDeletionPreview refused = services.DemoData.Preview();
                long backupsBefore = BackupCount(services);
                string hashBefore = DatabaseHash(db, "BackupRecords");
                var deleteResult = services.DemoData.Delete();
                r.AppendLine();
                r.AppendLine("[2] ONLY ACTIVE COMPANY → IsOnlyCompany=" + refused.IsOnlyCompany + " ; Delete() → " +
                             (deleteResult.IsFailure ? "REFUSED (" + deleteResult.ErrorCode + ")" : "DELETED (!!)"));
                r.AppendLine("    message: " + deleteResult.Error?.Replace(Environment.NewLine, " / "));
                r.AppendLine("    database hash before/after the refusal: " + hashBefore.Substring(0, 16) + " / " + DatabaseHash(db, "BackupRecords").Substring(0, 16));
                r.AppendLine("    backups before/after: " + backupsBefore + " / " + BackupCount(services) + " (no backup is taken for a refused removal)");
                Assert.That(refused.IsOnlyCompany, Is.True);
                Assert.That(deleteResult.IsFailure, Is.True);
                Assert.That(DatabaseHash(db, "BackupRecords"), Is.EqualTo(hashBefore));
                foreach (string lang in new[] { "fr", "ar" })
                {
                    services.Localization.SetLanguage(lang);
                    ShotWindow(new DemoRemovalPreviewViewModel(refused, lang == "ar"), "preview-only-company-" + lang, services);
                }
                services.Localization.SetLanguage("fr");

                // ---------------------------------------------------------- 3. the customer creates its company
                long real = services.Companies.Create(new Company
                {
                    NameFr = "SARL Bensaïd Distribution", Nif = "001216098765432", Nis = "001216098765432",
                    AddressFr = "12 rue Larbi Ben M'hidi, Alger"
                }).Value;
                // As in the app: the new company is created while the demo is open, then the user switches to it
                // in the header and adds the staff there.
                services.CompanyContext.Reload();
                services.CompanyContext.Active = services.CompanyContext.Companies.First(c => c.Id == real);
                foreach (var p in new[] { ("BENSAÏD", "Amel"), ("KHELIFI", "Mehdi") })
                {
                    var created = services.Employees.Create(new Employee
                    {
                        CompanyId = real, LastNameFr = p.Item1, FirstNameFr = p.Item2, Department = "Ventes", Poste = "Commercial",
                        Gender = Gender.Female, MaritalStatus = MaritalStatus.Single, PaymentMode = PaymentMode.Cash,
                        ContractType = ContractType.Cdi, HireDate = new DateTime(2025, 3, 1), BaseSalary = 52000m, IsActive = true
                    });
                    Assert.That(created.IsSuccess, Is.True, created.Error);
                }
                services.CompanyContext.Reload();
                services.CompanyContext.Active = services.CompanyContext.Companies.First(c => c.Id == demo.Id); // the demo is OPEN
                string realBefore = RealCompanyHash(db, real);
                long globalLeaveTypes = Count(db, "SELECT COUNT(*) FROM LeaveTypes WHERE CompanyId IS NULL");

                // ---------------------------------------------------------- 4. who sees the button
                var settings = new SettingsViewModel(services);
                settings.OnActivated();
                bool adminSees = settings.CanRemoveDemo;
                IAccessContext realAccess = services.Authorization;
                services.Authorization = new FakeAccess { IsEnforced = true, CurrentRole = UserRole.Hr };
                settings.OnActivated();
                bool hrSees = settings.CanRemoveDemo;
                services.Authorization = realAccess;
                settings.OnActivated();
                r.AppendLine();
                r.AppendLine("[3] BUTTON VISIBILITY → administrator: " + (adminSees ? "shown" : "hidden") + " ; HR role: " + (hrSees ? "shown" : "hidden"));
                Assert.That(adminSees, Is.True);
                Assert.That(hrSees, Is.False);

                foreach (string lang in new[] { "fr", "ar" })
                {
                    services.Localization.SetLanguage(lang);
                    settings.OnActivated();
                    typeof(SettingsViewModel).GetProperty("DeviceIdText").SetValue(settings, "•••• (masqué)"); // never publish this PC's device id
                    Shot(new SettingsView { DataContext = settings }, 1100, "settings-" + lang, services, 3000);
                }
                services.Localization.SetLanguage("fr");

                // ---------------------------------------------------------- 5. remove through the REAL button flow
                DemoRemovalPreviewViewModel shown = null;
                string outcome = null;
                settings.ConfirmDemoRemoval = vm => { shown = vm; return true; };
                settings.ShowDemoMessage = m => outcome = m;
                settings.RemoveDemoCommand.Execute(null);
                Assert.That(shown, Is.Not.Null, "the preview was shown");
                Assert.That(outcome, Is.Not.Null.And.Contains("supprimées"), outcome);

                foreach (string lang in new[] { "fr", "ar" })
                {
                    services.Localization.SetLanguage(lang);
                    // Rebuild the preview from a preview of the backup copy (same numbers) for the screenshot language.
                    ShotWindow(Relabel(shown, lang == "ar"), "preview-" + lang, services);
                }
                services.Localization.SetLanguage("fr");

                r.AppendLine();
                r.AppendLine("[4] PREVIEW shown before deletion (" + shown.CompanyName + "):");
                foreach (DemoRemovalRow row in shown.Rows) r.AppendLine(string.Format(Inv, "    {0,-50}{1,8}", row.Label, row.CountText));
                r.AppendLine("    " + shown.TotalText);

                string backup = Directory.GetFiles(services.Configuration.BackupDirectory, "optipaie_*.db").OrderBy(f => new FileInfo(f).LastWriteTimeUtc).Last();
                Assert.That(outcome, Does.Contain(Path.GetFileName(backup)), "the message names the backup that was taken");
                r.AppendLine();
                r.AppendLine("[5] BACKUP taken BEFORE deletion: " + Path.GetFileName(backup) + "  (" + new FileInfo(backup).Length.ToString("N0", Fr) + " bytes)");
                r.AppendLine("    backup still holds the demo: company rows=" + Count(backup, "SELECT COUNT(*) FROM Companies WHERE Id = " + demo.Id) +
                             ", employees=" + Count(backup, "SELECT COUNT(*) FROM Employees WHERE CompanyId = " + demo.Id) + "  → restorable");

                r.AppendLine();
                r.AppendLine("[6] RESULT");
                r.AppendLine("    message shown: " + outcome.Replace("\n", " / "));
                r.AppendLine("    open company after removal: #" + services.CompanyContext.ActiveId + " « " + services.CompanyContext.ActiveName + " » (was the demo #" + demo.Id + ")");
                r.AppendLine("    demo still recognised: " + (services.DemoData.FindDemoCompany() != null));
                r.AppendLine("    rows still referencing the demo, per plan table:");
                long leftover = 0;
                using (IUnitOfWork uow = new OptiPaie.Data.Context.UnitOfWorkFactory(new OptiPaie.Data.Context.SqliteConnectionFactory(db)).Create())
                    foreach (TableRows t in uow.DemoData.CountDependents(demo.Id)) { leftover += t.Rows; if (t.Rows != 0) r.AppendLine("      " + t.Table + " = " + t.Rows); }
                r.AppendLine("      total = " + leftover);
                IReadOnlyList<string> orphans = Orphans(db);
                r.AppendLine("    orphan scan (declared FKs + every *Id column → its parent): " + (orphans.Count == 0 ? "0 orphans" : string.Join("; ", orphans)));
                r.AppendLine("    customer company rows hash before/after: " + realBefore.Substring(0, 16) + " / " + RealCompanyHash(db, real).Substring(0, 16));
                r.AppendLine("    global leave types (CompanyId NULL) before/after: " + globalLeaveTypes + " / " + Count(db, "SELECT COUNT(*) FROM LeaveTypes WHERE CompanyId IS NULL"));
                r.AppendLine("    soft-deleted companies #1 and #2 untouched: " + Count(db, "SELECT COUNT(*) FROM Companies WHERE Id IN (1,2)"));

                Assert.That(services.CompanyContext.ActiveId, Is.EqualTo(real));
                Assert.That(services.DemoData.FindDemoCompany(), Is.Null);
                Assert.That(leftover, Is.EqualTo(0));
                Assert.That(orphans, Is.Empty);
                Assert.That(RealCompanyHash(db, real), Is.EqualTo(realBefore));
                Assert.That(Count(db, "SELECT COUNT(*) FROM LeaveTypes WHERE CompanyId IS NULL"), Is.EqualTo(globalLeaveTypes));

                File.WriteAllText(Path.Combine(_out, "proof-demo-real-db-copy.txt"), r.ToString(), new UTF8Encoding(true));
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        [Test]
        public void Licence_Updated_Message_Real_MessageBox()
        {
            // Exactly what the shell shows (ShellViewModel.AnnounceLicenseChange → Dialogs.Info) on a Mono → Multi change.
            Capture(LicenseScopeChange.BecameMultiAr + Environment.NewLine + LicenseScopeChange.BecameMultiFr, "licence-updated-multi");
            Capture(LicenseScopeChange.BecameMonoAr + Environment.NewLine + LicenseScopeChange.BecameMonoFr, "licence-updated-mono");
        }

        // ------------------------------------------------------------------ helpers

        private void Capture(string message, string name)
        {
            const string caption = "Licence · الترخيص";
            string path = Path.Combine(_out, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            Task grab = Task.Run(() =>
            {
                for (int i = 0; i < 150; i++)
                {
                    IntPtr h = FindWindow("#32770", caption);
                    if (h != IntPtr.Zero && IsWindowVisible(h))
                    {
                        for (int attempt = 0; attempt < 5; attempt++)
                        {
                            Thread.Sleep(700);
                            GetWindowRect(h, out RECT rc);
                            int w = rc.Right - rc.Left, ht = rc.Bottom - rc.Top;
                            if (w < 50 || ht < 50) continue;
                            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), bmp = CreateCompatibleBitmap(screen, w, ht);
                            IntPtr old = SelectObject(mem, bmp);
                            PrintWindow(h, mem, 2);
                            SelectObject(mem, old);
                            BitmapSource src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            src.Freeze();
                            DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
                            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(src));
                            using (var fs = new FileStream(path, FileMode.Create)) enc.Save(fs);
                            if (new FileInfo(path).Length > 5000) break; // a blank (not yet painted) frame is tiny: retry
                        }
                        PostMessage(h, 0x0111 /* WM_COMMAND */, (IntPtr)1 /* IDOK */, IntPtr.Zero);
                        return;
                    }
                    Thread.Sleep(100);
                }
            });
            Dialogs.Info(message, caption); // the real, blocking message box; closed by the grabber
            grab.Wait(15000);
            Assert.That(File.Exists(path) && new FileInfo(path).Length > 5000, Is.True, name + " captured");
        }

        private static DemoRemovalPreviewViewModel Relabel(DemoRemovalPreviewViewModel shown, bool arabic) =>
            new DemoRemovalPreviewViewModel(new DemoDeletionPreview
            {
                Company = new DemoCompanyInfo { Name = shown.CompanyName, MatchedBy = "fingerprint" },
                Rows = shown.Rows.Select(row => new TableRows(TableOf(row.Label), row.Count)).ToList(),
                IsOnlyCompany = shown.IsOnlyCompany
            }, arabic);

        private static string TableOf(string label)
        {
            foreach (string t in new[]
            {
                "Employees", "Payslips", "PayrollDetails", "PayrollRuns", "ArchiveDocuments", "Loans", "LoanRepayments", "AttendanceRecords",
                "LeaveRequests", "EmploymentContracts", "WorkCertificates", "EmployeeElements", "BehaviorLogs", "Departments", "EvaluationScores",
                "Evaluations", "EvalCriteria", "EvalTemplates", "EvalPeriods", "Interviews", "CandidateAttachments", "Candidates", "JobPostings",
                "TrainingParticipants", "TrainingSessions", "AssetAssignments", "Assets", "Holidays", "LeaveTypes", "CompanyWorkCalendar",
                "AuditLog", "Companies", "Candidates.HiredEmployeeId"
            })
                if (DemoRemovalPreviewViewModel.LabelOf(t, false) == label) return t;
            return label;
        }

        private void ShotWindow(DemoRemovalPreviewViewModel vm, string name, AppServices services)
        {
            var window = new DemoRemovalWindow { DataContext = vm };
            var body = (FrameworkElement)window.Content;
            window.Content = null;
            var frame = new Border
            {
                Width = 540, BorderThickness = new Thickness(1), DataContext = vm,
                BorderBrush = (Brush)Application.Current.Resources["BorderStrong"], Child = body
            };
            Shot(frame, 540, name, services, 1400);
            frame.Child = null;
            window.Close();
        }

        private void Shot(FrameworkElement view, double width, string name, AppServices services, double maxHeight)
        {
            var host = new Border
            {
                Background = (Brush)Application.Current.Resources["Canvas"],
                FlowDirection = services.Localization.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                Width = width, Child = view
            };
            // Two passes: item templates (the preview list) are generated during the first layout.
            double h = 120;
            for (int pass = 0; pass < 3; pass++)
            {
                host.Measure(new Size(width, double.PositiveInfinity));
                h = Math.Min(maxHeight, Math.Max(120, host.DesiredSize.Height));
                host.Arrange(new Rect(0, 0, width, h)); host.UpdateLayout();
            }
            const double scale = 1.5;
            var rtb = new RenderTargetBitmap((int)(width * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(host);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = new FileStream(Path.Combine(_out, name + ".png"), FileMode.Create)) enc.Save(fs);
            host.Child = null;
        }

        private static long BackupCount(AppServices services) =>
            Directory.Exists(services.Configuration.BackupDirectory) ? Directory.GetFiles(services.Configuration.BackupDirectory, "*.db").Length : 0;

        private static IReadOnlyList<string> Orphans(string db)
        {
            var found = new List<string>();
            var parents = new Dictionary<string, string>
            {
                ["CompanyId"] = "Companies", ["EmployeeId"] = "Employees", ["HiredEmployeeId"] = "Employees",
                ["PayslipId"] = "Payslips", ["RunId"] = "PayrollRuns", ["LoanId"] = "Loans", ["CandidateId"] = "Candidates",
                ["PostingId"] = "JobPostings", ["SessionId"] = "TrainingSessions", ["AssetId"] = "Assets", ["EvaluationId"] = "Evaluations"
            };
            using (var c = Open(db))
            {
                found.AddRange(Query(c, "SELECT \"table\" || ' -> ' || parent FROM pragma_foreign_key_check;"));
                foreach (string table in Query(c, "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';"))
                    foreach (string col in Query(c, "SELECT name FROM pragma_table_info('" + table + "');"))
                    {
                        if (!parents.TryGetValue(col, out string parent) || parent == table) continue;
                        long n = Convert.ToInt64(Exec(c, "SELECT COUNT(*) FROM " + table + " WHERE " + col + " IS NOT NULL AND " + col + " <> 0 AND " +
                                                         col + " NOT IN (SELECT Id FROM " + parent + ");"), Inv);
                        if (n > 0) found.Add(table + "." + col + " -> " + parent + " : " + n);
                    }
            }
            return found;
        }

        private static string RealCompanyHash(string db, long real)
        {
            string e = "(SELECT Id FROM Employees WHERE CompanyId = " + real + ")";
            var sb = new StringBuilder();
            using (var c = Open(db))
                foreach (string sql in new[]
                {
                    "SELECT * FROM Companies WHERE Id = " + real, "SELECT * FROM Employees WHERE CompanyId = " + real + " ORDER BY Id",
                    "SELECT * FROM Departments WHERE CompanyId = " + real + " ORDER BY Id",
                    "SELECT * FROM EmploymentContracts WHERE EmployeeId IN " + e + " ORDER BY Id"
                })
                    sb.Append(Dump(c, sql));
            return Sha(sb.ToString());
        }

        private static string DatabaseHash(string db, string skip)
        {
            var sb = new StringBuilder();
            using (var c = Open(db))
                foreach (string t in Query(c, "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;"))
                    if (t != skip) sb.Append("#" + t + "\n" + Dump(c, "SELECT * FROM " + t + " ORDER BY rowid"));
            return Sha(sb.ToString());
        }

        private static string Dump(SQLiteConnection c, string sql)
        {
            var sb = new StringBuilder();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = sql;
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                    {
                        for (int i = 0; i < rd.FieldCount; i++) sb.Append(Convert.ToString(rd.GetValue(i), Inv)).Append('|');
                        sb.Append('\n');
                    }
            }
            return sb.ToString();
        }

        private static List<string> Rows(string db, string sql) { using (var c = Open(db)) return Query(c, sql); }
        private static long Count(string db, string sql) { using (var c = Open(db)) return Convert.ToInt64(Exec(c, sql), Inv); }
        private static string Scalar(string db, string sql) { using (var c = Open(db)) return Convert.ToString(Exec(c, sql), Inv); }

        private static List<string> Query(SQLiteConnection c, string sql)
        {
            var list = new List<string>();
            using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; using (var rd = cmd.ExecuteReader()) while (rd.Read()) list.Add(Convert.ToString(rd.GetValue(0), Inv)); }
            return list;
        }

        private static object Exec(SQLiteConnection c, string sql) { using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; return cmd.ExecuteScalar(); } }

        private static SQLiteConnection Open(string path) { var c = new SQLiteConnection("Data Source=" + path + ";Version=3;"); c.Open(); return c; }

        private static string Sha(string s)
        {
            using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").ToLowerInvariant();
        }

        private static string FindRepoRoot()
        {
            var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "OptiPaie.sln"))) d = d.Parent;
            return d?.FullName ?? Directory.GetCurrentDirectory();
        }

        private sealed class FakeAccess : IAccessContext
        {
            public bool IsEnforced { get; set; }
            public UserRole CurrentRole { get; set; }
            public bool IsOwner { get; set; }
            public long? CurrentUserId { get; set; }
        }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    }
}
