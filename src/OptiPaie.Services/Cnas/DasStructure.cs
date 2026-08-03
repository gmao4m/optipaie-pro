using System;
using System.Text;

namespace OptiPaie.Services.Cnas
{
    /// <summary>
    /// Post-write verification: re-read the bytes actually written to disk and prove they still
    /// match the DAS layout — pure ASCII, no BOM, header = one HeaderLength line + a trailing
    /// CR LF, detail = DetailLength lines joined by CR LF with NO trailing CR LF. If any check
    /// fails, the caller deletes the files rather than leave a corrupt deposit.
    /// </summary>
    public static class DasStructure
    {
        public static bool Verify(byte[] header, byte[] detail, out string reason)
        {
            return VerifyHeader(header, out reason) && VerifyDetail(detail, out reason);
        }

        private static bool VerifyHeader(byte[] header, out string reason)
        {
            reason = null;
            if (header == null || header.Length == 0) { reason = "entête vide"; return false; }
            if (HasBom(header)) { reason = "entête : présence d'un BOM"; return false; }
            if (!IsAscii(header)) { reason = "entête : caractère non-ASCII"; return false; }
            if (header.Length != DasFileSpec.HeaderLength + 2)
            {
                reason = "entête : " + header.Length + " octets (attendu " + (DasFileSpec.HeaderLength + 2) + ")";
                return false;
            }
            if (!(header[header.Length - 2] == (byte)'\r' && header[header.Length - 1] == (byte)'\n'))
            {
                reason = "entête : pas de CR LF final";
                return false;
            }
            return true;
        }

        private static bool VerifyDetail(byte[] detail, out string reason)
        {
            reason = null;
            if (detail == null || detail.Length == 0) { reason = "détail vide"; return false; }
            if (HasBom(detail)) { reason = "détail : présence d'un BOM"; return false; }
            if (!IsAscii(detail)) { reason = "détail : caractère non-ASCII"; return false; }
            if (detail.Length >= 2 && detail[detail.Length - 2] == (byte)'\r' && detail[detail.Length - 1] == (byte)'\n')
            {
                reason = "détail : CR LF final interdit";
                return false;
            }

            string[] lines = Encoding.ASCII.GetString(detail).Split(new[] { "\r\n" }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                if (line.Length != DasFileSpec.DetailLength)
                {
                    reason = "détail : une ligne de " + line.Length + " caractères (attendu " + DasFileSpec.DetailLength + ")";
                    return false;
                }
            }
            return true;
        }

        private static bool HasBom(byte[] b)
        {
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return true;              // UTF-8
            if (b.Length >= 2 && ((b[0] == 0xFF && b[1] == 0xFE) || (b[0] == 0xFE && b[1] == 0xFF))) return true; // UTF-16
            return false;
        }

        private static bool IsAscii(byte[] b)
        {
            foreach (byte x in b)
            {
                if (x > 127) return false;
            }
            return true;
        }
    }
}
