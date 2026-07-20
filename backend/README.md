# Centralized Licensing Platform — Backend (Phase 1)

A free-tier **Supabase** backend that licenses **multiple desktop products** from
one place (Payroll today; Fleet, Accounting, ATS, Construction, … later) with no
future redesign. One cloud, one auth, one audit log, one licensing engine.

It stores **license metadata only** — never employees, payroll, salaries or any
HR business data (that always stays local in each customer's SQLite database).
Each desktop app talks to this backend **only** through the abstract
`ILicenseBackend` contract and receives permissions for **its own product**;
apps never know about each other. The whole backend can be swapped for another
provider later without touching the desktop apps.

```
backend/
  scripts/generate-keypair.ts          Ed25519 signing keypair generator
  supabase/
    migrations/
      0001_licensing_schema.sql        products + modules + licenses + audit
      0002_rls_policies.sql            Row-Level Security
      0003_admin_views.sql             dashboard stat views (Phase 4)
    functions/
      _shared/                         cors / service client / ed25519 / license logic
      activate/index.ts                POST /activate  (bind device, signed token)
      validate/index.ts                POST /validate  (refresh token, sync modules)
    seed/test_license.sql              one test license for end-to-end testing
```

## Multi-product model
- **products** — each desktop product: `key`, `name`, `key_prefix` (license-key
  prefix, e.g. `PAY`/`FLT`/`ACC`), `current_version`, and `activation_rules`
  (jsonb, e.g. `{"graceDays":30,"bindDevice":true,"maxDevices":1}`) so each
  product defines **its own activation rules** with no schema change.
- **modules** — scoped per product (`primary key (product_id, key)`). Each
  product has **its own modules**.
- **licenses** — each belongs to a product; keys are globally unique. Each
  product has **its own customers/licenses**.
- The signed token carries `product`, so a desktop app **rejects** a token for
  another product; the API also refuses a key that belongs to a different product
  (`wrong_product`).

**Adding a future product = one INSERT into `products` + its modules.** Nothing
else changes — same auth, security, engine, audit log, backend.

---

## What you need to do (one-time setup)

### 1. Create the Supabase project (free)
1. <https://supabase.com> → **New project** (free tier), region close to Algeria.
2. Save the DB password.
3. **Project Settings → API**: copy **Project URL**, **anon public key**,
   **service_role key**. (URL + anon key go into each desktop app's config in
   Phase 2; the service_role key stays only in Supabase.)

### 2. Create the schema
In the **SQL Editor**, run in order:
1. `supabase/migrations/0001_licensing_schema.sql`
2. `supabase/migrations/0002_rls_policies.sql`
3. `supabase/migrations/0003_admin_views.sql`

Then **Authentication → Providers**: disable public sign-ups, and create **one**
owner user (**Authentication → Users**) — your admin login.

### 3. Generate the signing keypair
Install Deno (<https://deno.com>), then:
```bash
deno run backend/scripts/generate-keypair.ts
```
Copy the **PRIVATE key** (step 4) and keep the **PUBLIC key** (embedded in each
desktop app in Phase 2). Store both in a password manager; never commit the
private key. Keep the **same** keypair forever — even across a provider
migration — so activated customers keep working.

### 4. Deploy the Edge Functions
Install the Supabase CLI, then from the repo root:
```bash
supabase login
supabase link --project-ref <your-project-ref>
supabase secrets set LICENSE_SIGNING_PRIVATE_KEY=<private-hex>
supabase functions deploy activate
supabase functions deploy validate
```
`SUPABASE_URL` / `SUPABASE_SERVICE_ROLE_KEY` are injected automatically.

### 5. Test end-to-end
Run `supabase/seed/test_license.sql`; note the printed `TEST LICENSE KEY`.
```bash
# Activate (note productKey — the app always sends its own):
curl -X POST "https://<project-ref>.functions.supabase.co/activate" \
  -H "Authorization: Bearer <anon-key>" -H "Content-Type: application/json" \
  -d '{"productKey":"payroll","licenseKey":"<TEST KEY>","companyName":"Entreprise Test SARL","email":"test@example.com","deviceId":"DEVICE-TEST-001","appVersion":"1.0.0"}'
# -> {"token":"<b64>.<b64>","status":"active","modules":["payroll","ats"]}

# Validate:
curl -X POST "https://<project-ref>.functions.supabase.co/validate" \
  -H "Authorization: Bearer <anon-key>" -H "Content-Type: application/json" \
  -d '{"productKey":"payroll","licenseKey":"<TEST KEY>","deviceId":"DEVICE-TEST-001"}'
```
Checks: wrong `deviceId` → `409 device_mismatch`; `productKey":"fleet"` →
`wrong_product`; flip `ats` off in `license_modules` → validate drops `ats`.
See `license.generate/activate/validate` rows in **audit_log**.

---

## The signed token (verified offline by the desktop)
`base64url(payloadJson) + "." + base64url(signature)`; signature is over the
ASCII bytes of the left segment. Payload:
```json
{
  "v": 1,
  "product": "payroll", "productVersion": "1.0.0",
  "licenseKey": "PAY-8X2Q-7A9L-5D3K",
  "companyName": "…", "companyId": null, "email": "…",
  "deviceId": "…", "status": "active",
  "modules": ["payroll", "ats"],
  "issuedAt": "…Z", "expiresAt": null, "graceUntil": "…Z (issuedAt + graceDays)"
}
```
The desktop verifies with the embedded **public** key and checks `product`
matches its own; any edit to the cached license breaks the signature → the app
falls back to core-only.

## Admin Panel (Phase 4) — already supported by this schema
- **Dashboard** (per product, via `v_product_stats` + `v_module_activation_stats`):
  total customers, active/suspended/total licenses, new customers this month,
  module-activation stats. Product switcher shares one auth/backend/audit.
- **Customer page**: company, email, phone, license key, activation date, last
  sync, device info, enabled modules, internal notes, full audit history.
- **Features**: fast search (GIN index on name/email/phone/key), filters, export
  to Excel, DB backup, device reset (clear `device_id`), one-click module toggle.
  These become server-side admin RPCs (with audit logging) + the SPA in Phase 4.

## Bulk generate & print licenses
Generate many keys at once and print them onto A4 sheets (one license per cell).

**1. Generate** — in the Supabase SQL editor (function added by `0004_admin_functions.sql`):
```sql
-- 100 Payroll keys (status pending, core module on, others off, each audited):
select * from generate_licenses(100, 'payroll', 'Lot janvier 2026');
```
Returns the `license_key` column. Keys are `PAY-XXXX-XXXX-XXXX` (15 alphanumerics).
Copy the whole column.

**2. Print** — open `backend/admin/print-licenses.html` in any browser (no server
needed), paste the keys (one per line), pick a layout, then **Imprimer / PDF**:
- 2 columns → **14 per A4 page** (largest text)
- 3 columns → **18 per A4 page**
- 4 columns → **20 per A4 page** (default)

Each license sits in its own bordered cell with the key in large monospace, ready
to cut and hand to customers. (In Phase 4 the admin panel will do both steps in the
UI; this tool works today.)

Pool licenses start with a placeholder company name; when a customer activates one,
`activate` adopts the real company name they enter.

## Security notes
- The **anon key** in the apps has **no table access** (RLS denies it) — only
  function calls.
- The **service_role key** and **private signing key** live only in Supabase.
- License keys use an unambiguous charset (no `0/O`, `1/I`): `PREFIX-XXXX-XXXX-XXXX`.
