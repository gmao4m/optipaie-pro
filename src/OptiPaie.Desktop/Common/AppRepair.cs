using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OptiPaie.Desktop.Common
{
    /// <summary>
    /// Locates the installed product (by our MSI UpgradeCode) so a missing-file repair can be
    /// run from the Windows Installer cache. Returns null when the app was not installed via our
    /// MSI (nothing that <c>msiexec</c> can repair).
    /// </summary>
    public static class AppRepair
    {
        // MUST match installer/Package.wxs <Package UpgradeCode="..."> and Bundle is unrelated.
        private const string UpgradeCode = "{B4318537-7007-4E89-AFE5-148F2A869C99}";

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern int MsiEnumRelatedProducts(string lpUpgradeCode, int dwReserved, int iProductIndex, StringBuilder lpProductBuf);

        /// <summary>The installed ProductCode for our UpgradeCode, or null if none / on error.</summary>
        public static string FindProductCode()
        {
            try
            {
                var buffer = new StringBuilder(39); // {GUID}\0
                int rc = MsiEnumRelatedProducts(UpgradeCode, 0, 0, buffer);
                return rc == 0 ? buffer.ToString() : null; // 0 = ERROR_SUCCESS
            }
            catch
            {
                return null;
            }
        }
    }
}
