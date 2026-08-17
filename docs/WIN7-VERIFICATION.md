# Windows 7 SP1 — Verification checklist (run on a real machine)

**Who runs this:** you, on an actual **Windows 7 SP1 (32-bit or 64-bit)** machine.

**Why this exists:** I (the assistant) **cannot test on Windows 7 from my environment.**
Every "Expected" below is derived from the **source code and binary analysis**, *not* from a
real Windows 7 run. Treat this as **unverified until you complete it**. The one thing already
proven by binary inspection is that the installer contains **no `GetDpiForSystem` import** (the
defect that broke the old Velopack installer); everything else needs a live pass.

**Artefact under test:**
`installer/output/OptiPaie-PRO-Setup.exe` — SHA256 `0543d2e7b62dc465b1ba63f81d039e26c8300787dfebab8b70fafa755f29ba18` (version **1.26.0**). Confirm this hash on the machine before installing.

**Where the log lives:** `%AppData%\OptiPaie DZ\optipaie.log` (open it after any failure).

---

## Step 1 — Install and first launch

**Action**
1. Copy `OptiPaie-PRO-Setup.exe` to the Win 7 machine. Confirm its SHA256 (right-click → if you have a hash tool, or `certutil -hashfile "OptiPaie-PRO-Setup.exe" SHA256`).
2. Double-click it. If Windows shows a .NET Framework 4.8 prompt, let it install (needs internet the first time; ~2–5 min).
3. Finish the wizard; launch OptiPaie PRO from the Desktop shortcut.

**Expected** *(from analysis, not a Win 7 run)*
- The installer window **opens** (no "point d'entrée introuvable / entry point not found" error — that was the old Velopack bug).
- The app opens to its normal start screen. On an existing client, prior data appears and **no licence is re-requested**.

**If it fails, send me:**
- A photo of the exact error dialog (full text/title).
- `certutil -hashfile` output (to confirm the right file).
- Windows version: run `winver` and screenshot.
- `%AppData%\OptiPaie DZ\optipaie.log`.

---

## Step 2 — Open and render one payslip (Skia on Windows 7)

This exercises the native `libSkiaSharp.dll` / `libHarfBuzzSharp.dll` (QuestPDF), which is the second thing that could be Win 7-sensitive.

**Action**
1. Open an employee, generate/open one **fiche de paie** (payslip) as PDF.

**Expected** *(from analysis)*
- The PDF opens and looks correct (amounts, layout). SkiaSharp 2.88.x is Windows 7-compatible, so this **should** work — but this is exactly what the live test confirms.

**If it fails, send me:**
- The error (photo) and `optipaie.log`.
- Whether the app **crashes** vs shows an error vs produces a blank/garbled PDF.
- A common Win 7 cause is a missing **Visual C++ runtime**: if the log mentions `libSkiaSharp`/`DllNotFoundException` or `0xc000007b`, tell me — the fix is installing the VC++ 2015–2022 x86 redistributable.

---

## Step 3 — Update-check button (TLS 1.2 — the silent-failure trap)

The button «التحقق من التحديثات» fetches `version.json` over **HTTPS/TLS 1.2** from
`raw.githubusercontent.com`. GitHub refuses TLS 1.0/1.1. **Windows 7 SP1 supports TLS 1.2 but
does not always have it enabled at the OS level.** The app forces TLS 1.2/1.3 at the process
level, but that only works if the OS SChannel provider has TLS 1.2 available.

⚠️ **A TLS failure here is silent**: the code treats any HTTPS error as "no update", so the
button will say **you are up to date even when you are not** — the same disease as the install
bug, just quieter. So test it deliberately.

**Action**
1. First, a quick OS probe: open **Internet Explorer** on the Win 7 box and go to
   `https://raw.githubusercontent.com/gmao4m/optipaie-pro/main/version.json`.
   - If the page shows the JSON text → OS TLS is fine.
   - If IE shows "cannot display the page / secure channel" → TLS 1.2 is not enabled (apply the fix below).
2. In the app, click **التحقق من التحديثات** while the installed version is **older** than 1.26.0.

**Expected** *(from analysis)*
- The app detects **1.26.0** and offers to update.
- If it instead says "up to date" on an older build → **suspect TLS**, not "no update". Go to the fix.

**The fix (enable TLS 1.2 on Windows 7 SP1) — apply, then reboot:**
Save this as `enable-tls12.reg`, right-click → Merge, then **reboot**:
```
Windows Registry Editor Version 5.00

[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client]
"DisabledByDefault"=dword:00000000
"Enabled"=dword:00000001

[HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Server]
"DisabledByDefault"=dword:00000000
"Enabled"=dword:00000001

[HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\.NETFramework\v4.0.30319]
"SystemDefaultTlsVersions"=dword:00000001
"SchUseStrongCrypto"=dword:00000001

[HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\.NETFramework\v4.0.30319]
"SystemDefaultTlsVersions"=dword:00000001
"SchUseStrongCrypto"=dword:00000001
```
- The **WOW6432Node** entry is the one that matters for our **32-bit** app — do not skip it.
- On a fully-patched Win 7 SP1 these are often already correct; the `.reg` is idempotent.

**If it still fails after the fix, send me:**
- Whether IE could open the `version.json` URL (yes/no).
- `optipaie.log` (look for a line like `version.json HTTP ...` or a handshake exception).
- Whether Windows Update is current (very old Win 7 may lack the TLS 1.2 cipher updates — KB3140245).

---

## Step 4 — Print a payslip to a real printer

**Action**
1. Open a payslip and use the print function to send it to a **physical printer** installed on the Win 7 machine.

**Expected** *(from analysis)*
- The Windows print dialog appears; the payslip prints with correct layout and Arabic/French text.

**If it fails, send me:**
- Does the print **dialog** appear at all? (If not, it is the print path; if yes but output is wrong, it is rendering.)
- A photo of the printed page (or the print-preview) showing the problem.
- Printer make/model and whether other apps print fine on that machine.

---

## Step 5 — Arabic rendering (UI and PDF)

**Action**
1. Switch the app UI to **Arabic** (العربية). Check menus, buttons, and a data screen (e.g. Congés/leave list) — text is right-to-left and shaped correctly (letters joined, not isolated boxes).
2. Generate a payslip / a leave document PDF that contains Arabic and open it.

**Expected** *(from analysis)*
- UI: Arabic reads right-to-left, correctly shaped (this uses the bundled IBM Plex / app fonts + WPF, Win 7-supported).
- PDF: Arabic in the PDF is correctly shaped and positioned (HarfBuzz shaping via `libHarfBuzzSharp.dll`).

**If it fails, send me:**
- A screenshot of the UI screen and/or the PDF showing the problem.
- Whether the letters are **isolated/boxed** (font/shaping issue) vs **left-to-right** (RTL issue) vs **missing** (font not found).

---

## After the run — summary to send me

For each of the 5 steps: **PASS / FAIL**, plus for any FAIL the items listed under it and always
`%AppData%\OptiPaie DZ\optipaie.log`. If all 5 pass, the WiX installer + app are **confirmed** on
Windows 7 SP1 and this client (and future Win 7 clients) can be migrated with
`docs/CLIENT-WIN7-MIGRATION.md`.
