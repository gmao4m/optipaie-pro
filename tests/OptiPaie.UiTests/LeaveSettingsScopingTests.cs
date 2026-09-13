using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using OptiPaie.Core.Entities;
using OptiPaie.Core.Interfaces.Repositories;
using OptiPaie.Data.Context;
using OptiPaie.Data.Migrations;
using OptiPaie.Desktop.ViewModels;
using OptiPaie.Services;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// Guards the fix for the inert leave-regulatory checkboxes. The settings dialog's five
    /// per-company options (holidays, calendar-day count, July→June reference, unpaid accrual,
    /// strict CNAS) must be READ and SAVED scoped to the ACTIVE company. Historically the dialog used
    /// the company-agnostic overloads (company id 0), so what it saved was stored under ".0" while the
    /// leave/payroll calculation reads ".{companyId}" — the checkboxes had no effect.
    /// <para>
    /// The window has a runtime constructor and is NOT built by the render smoke test, so this test
    /// exercises the exact <see cref="LeaveSettingsViewModel"/> code the window uses, over a real
    /// <see cref="LeaveService"/> + temp SQLite database, proving both directions (read and write) are
    /// scoped and that one company's setting never leaks into another.
    /// </para>
    /// </summary>
    [TestFixture, Apartment(ApartmentState.STA)]
    public sealed class LeaveSettingsScopingTests
    {
        private string _dir;
        private UnitOfWorkFactory _uow;
        private LeaveService _leave;
        private long _companyA;
        private long _companyB;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "optipaie-lset-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SqliteTypeHandlers.Register();
            var factory = new SqliteConnectionFactory(Path.Combine(_dir, "test.db"));
            using (var c = factory.CreateOpenConnection()) new MigrationRunner(c).Run();

            _uow = new UnitOfWorkFactory(factory);
            _leave = new LeaveService(_uow);
            _companyA = InsertCompany("SARL A", "000000000000000");
            _companyB = InsertCompany("SARL B", "000000000000001");
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private long InsertCompany(string name, string nif)
        {
            using (IUnitOfWork uow = _uow.Create())
            {
                uow.BeginTransaction();
                long id = uow.Companies.Insert(new Company { NameFr = name, Nif = nif });
                uow.Commit();
                return id;
            }
        }

        [Test]
        public void Dialog_ReadsAndWritesRegulatoryFlags_ScopedToTheActiveCompany()
        {
            // WRITE — saving through the dialog opened for company A scopes to A, never touches B.
            var vmA = new LeaveSettingsViewModel(_leave, _companyA);
            vmA.ExcludeHolidays = true;
            vmA.CalendarDayCount = true;
            vmA.SaveCommand.Execute(null);

            Assert.That(_leave.GetSettings(_companyA).ExcludeHolidays, Is.True, "A : enregistré pour A");
            Assert.That(_leave.GetSettings(_companyA).CalendarDayCount, Is.True);
            Assert.That(_leave.GetSettings(_companyB).ExcludeHolidays, Is.False, "B : non affecté (portée par société)");
            Assert.That(_leave.GetSettings(_companyB).CalendarDayCount, Is.False);

            // READ — a dialog opened for A shows A's saved value; one opened for B shows the default.
            Assert.That(new LeaveSettingsViewModel(_leave, _companyA).ExcludeHolidays, Is.True,
                "le dialogue de A affiche l'option activée");
            Assert.That(new LeaveSettingsViewModel(_leave, _companyB).ExcludeHolidays, Is.False,
                "le dialogue de B affiche l'option désactivée");
        }
    }
}
