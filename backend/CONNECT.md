# Connect OptiPaie PRO to your Supabase project

Three steps. Everything is generated for you — you never create a table by hand.

The signing keypair is already generated and **verified end-to-end** against the
app's token verifier:
- **Public key** → already embedded in the app (`LicensingConfig.cs`).
- **Private key** → saved to `C:\Users\PC\optipaie-signing-key.private.txt`
  (outside the repo, never committed). You'll put it into a Supabase secret in
  Step 2, then delete the file.

---

## Step 1 — Create the database (one paste)

Open your project → **SQL Editor** → New query → paste the whole file
`backend/supabase/setup.sql` → **Run**.

That creates every table (`products`, `licenses`, `modules`, `license_modules`,
`customers`, `devices`, `activations`, `audit_log`), all indexes, RLS policies,
functions (`gen_license_key`, `generate_licenses`) and views (`license_logs`,
`v_product_stats`, …). It is idempotent — safe to re-run.

Then, once, harden auth: **Authentication → Providers → disable public sign-ups**,
and **Authentication → Users → Add user** (your owner account for the admin panel).

## Step 2 — Deploy the Edge Functions + signing secret

Install the Supabase CLI (https://supabase.com/docs/guides/cli), then from the
repo root run (replace `<PROJECT-REF>` with your project's ref):

```bash
supabase login
supabase link --project-ref <PROJECT-REF>

# Put the PRIVATE signing key into a secret (reads the file from Step 0):
supabase secrets set LICENSE_SIGNING_PRIVATE_KEY="$(cat 'C:/Users/PC/optipaie-signing-key.private.txt')"

# Deploy both functions. --no-verify-jwt is REQUIRED because the app authenticates
# with the new "publishable" key (sb_publishable_...), which is NOT a JWT. These are
# public activation endpoints; security is the license key + Ed25519-signed tokens.
# (SUPABASE_URL + SERVICE_ROLE_KEY are injected into the functions automatically.)
supabase functions deploy activate --no-verify-jwt
supabase functions deploy validate --no-verify-jwt
supabase functions deploy activate-module --no-verify-jwt
```

> Re-run `setup.sql` if you set up the DB before the module-activation update
> (it added `activation_keys` handling, `activated_at`, and the
> `revoke_activation_key` / `generate_module_keys` functions — all idempotent).

> If activation ever returns **401**, the JWT check is still on — re-run the deploy
> with `--no-verify-jwt`, or switch `Licensing.AnonKey` in App.config to the
> **legacy `anon` JWT key** (Project Settings → API → *Legacy API keys*).

Then **delete** `C:\Users\PC\optipaie-signing-key.private.txt` — it now lives only
in the Supabase secret. (If you ever lose it, generate a new keypair and update
the embedded public key.)

## Step 3 — Point the app at your project

Fill the **two public values** in `src/OptiPaie.Desktop/App.config`:

```xml
<add key="Licensing.BaseUrl" value="https://<PROJECT-REF>.supabase.co/functions/v1" />
<add key="Licensing.AnonKey" value="<your publishable key>" />
```

**Already done for your project** (`bajiomgtkpdqyvgpigsc`) — App.config now holds:
`https://bajiomgtkpdqyvgpigsc.supabase.co/functions/v1` and your `sb_publishable_…`
key. Just rebuild the app after Steps 1–2.

---

## Test it end-to-end

1. Create a test license (SQL editor):
   ```sql
   select * from generate_licenses(1, 'payroll', 'Test');
   ```
   Copy the returned key (format `XXXXX-XXXXX-XXXXX-XXXXX`).
2. Launch OptiPaie PRO → Activation window → paste the key → **Activer**.
   It should activate; **Settings → Licence** shows customer, type, device, modules.
3. In Supabase, check the `devices` and `activations` tables (and `license_logs`).

## Security recap
- **Never** put the Service Role Key or DB password in the app or share them; the
  service role is auto-injected into the Edge Functions only.
- The anon key and Project URL are public (RLS protects the tables); the app's
  anon key can only call the two functions, never read tables directly.
- License tokens are Ed25519-signed (verified offline) and the local cache is
  DPAPI-encrypted.

## Auto-update
Wired to **GitHub Releases** (`Update.GitHubRepo = gmao4m/optipaie-pro` in App.config).
Push a `vX.Y.Z` tag → the GitHub Action builds and publishes the installer; clients
check the repo's latest release and update. See `release/README.md`.
