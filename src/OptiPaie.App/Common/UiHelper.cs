using System;
using System.Globalization;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraSplashScreen;

namespace OptiPaie.App.Common
{
    /// <summary>
    /// Shared UI helpers: localized DevExpress dialogs, money formatting and a
    /// recursive right-to-left applier used when the language switches.
    /// </summary>
    public static class UiHelper
    {
        private static readonly CultureInfo MoneyCulture = CultureInfo.GetCultureInfo("fr-FR");

        public static void Info(string message, string caption)
        {
            XtraMessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void Error(string message, string caption)
        {
            XtraMessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static bool Confirm(string message, string caption)
        {
            return XtraMessageBox.Show(message, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        /// <summary>Formats an amount with thousands separators and a currency suffix.</summary>
        public static string Money(decimal value, string currency)
        {
            return value.ToString("N2", MoneyCulture) + " " + currency;
        }

        /// <summary>Runs a (short) blocking operation behind a wait indicator so the UI never looks frozen.</summary>
        public static void RunBusy(Action work)
        {
            SplashScreenManager.ShowDefaultWaitForm();
            try
            {
                work();
            }
            finally
            {
                SplashScreenManager.CloseDefaultWaitForm();
            }
        }

        /// <summary>Applies (or clears) right-to-left layout to a control tree.</summary>
        public static void ApplyRightToLeft(Control root, bool rightToLeft)
        {
            RightToLeft value = rightToLeft ? RightToLeft.Yes : RightToLeft.No;
            root.RightToLeft = value;

            if (root is Form form)
            {
                form.RightToLeftLayout = rightToLeft;
            }

            foreach (Control child in root.Controls)
            {
                ApplyRightToLeft(child, rightToLeft);
            }
        }
    }
}
