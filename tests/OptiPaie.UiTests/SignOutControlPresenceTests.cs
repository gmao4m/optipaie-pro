using System.IO;
using NUnit.Framework;

namespace OptiPaie.UiTests
{
    /// <summary>
    /// Guards the sidebar sign-out control so it cannot vanish silently again. MainWindow cannot be
    /// constructed head-less (its ctor builds a ShellViewModel that needs the composed services), so
    /// this asserts, at the XAML-source level, that MainWindow still declares the sign-out Button —
    /// bound to <c>SignOutCommand</c>, labelled with the <c>Shell_SignOut</c> resource, and with its
    /// visibility bound to <c>CanSignOut</c> (shown only when there is a session to end, never
    /// hard-collapsed). Deleting or renaming any of these fails the test.
    /// </summary>
    [TestFixture]
    public sealed class SignOutControlPresenceTests
    {
        private static string MainWindowXaml()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OptiPaie.sln"))) dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "repository root (OptiPaie.sln) not found");
            string path = Path.Combine(dir.FullName, "src", "OptiPaie.Desktop", "Shell", "MainWindow.xaml");
            Assert.That(File.Exists(path), Is.True, "MainWindow.xaml not found at " + path);
            return File.ReadAllText(path);
        }

        [Test]
        public void MainWindow_DeclaresTheSignOutButton_BoundAndLabelled()
        {
            string xaml = MainWindowXaml();

            Assert.That(xaml, Does.Contain("Command=\"{Binding SignOutCommand}\""),
                "The sidebar sign-out Button (Command=SignOutCommand) is missing from MainWindow — it must not vanish silently.");
            Assert.That(xaml, Does.Contain("Shell_SignOut"),
                "The sign-out button must keep its localized label (Shell_SignOut), so it stays labelled for what it does.");
            Assert.That(xaml, Does.Contain("{Binding CanSignOut"),
                "The sign-out button's visibility must stay bound to CanSignOut (shown when a session exists), never hard-coded.");
        }
    }
}
