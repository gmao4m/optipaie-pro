# OptiPaie PRO — Admin Panel

A self-contained, single-file admin console (`index.html`) for the licensing
platform. No build step, no dependencies to install — it loads the Supabase JS
client from a CDN and talks to your project with the **publishable** key
(Row-Level Security protects the data; only your authenticated owner account can
read/write).

## Run it
- **Locally:** double-click `admin/index.html` (or serve the folder) and log in.
- **Hosted (recommended):** drop the `admin/` folder on **Cloudflare Pages**,
  **Vercel**, or **Netlify** (all free) so you can reach it from any browser.

Log in with the **owner account** you created in Supabase
(*Authentication → Users*). Public sign-ups must stay disabled.

> Requires the database to exist: run `backend/supabase/setup.sql` once first
> (it now includes the admin-platform tables).

## Features
- **Dashboard** — total / active / expired / disabled licenses, active devices,
  new-this-month, latest activations, latest updates.
- **Licenses** — search (key/company/email), filter (status, type), sortable
  columns, pagination. Create · edit · enable/disable · **extend +30d** ·
  **reset devices** · delete.
- **Modules** — per-license ON/OFF toggles (Payroll is always on). Optional
  expiration column exists in the schema.
- **Bulk generator** — 10 / 50 / 100 / 500 / 1000 unique keys in one click, with
  **CSV export**.
- **Devices** — list every activated device (id, app version, activated, last
  sync, status) with per-device **reset**.
- **Updates** — publish a version (version, channel, mandatory, package URL,
  checksum, notes), set the **latest**, delete/rollback.

## Config
The Project URL and publishable key are embedded at the top of `index.html`
(both are public). To point at a different project, edit `SUPABASE_URL` /
`SUPABASE_KEY` there.

## Security
- The **service-role key is never used here** — the panel uses the publishable
  key + your authenticated session; RLS enforces owner-only access.
- License keys are generated server-side (`generate_licenses` RPC).
- Module activation keys use the `activation_keys` table + `generate_module_keys`
  function (single-use, per-module) — the desktop *consumption* of those keys is
  the next increment (see the project notes).
