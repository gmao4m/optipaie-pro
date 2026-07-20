-- ============================================================================
--  OptiPaie DZ — Licensing platform : Row-Level Security
-- ----------------------------------------------------------------------------
--  Security model (unchanged by the multi-product design):
--    * The DESKTOP APPS never touch these tables directly. They only call the
--      `activate` / `validate` Edge Functions, which run with the SERVICE ROLE
--      and bypass RLS. So the anon key shipped in every app gets NO table
--      access at all.
--    * The ADMIN PANEL logs in via Supabase Auth (single owner account) and acts
--      as the `authenticated` role, which is granted full access below. The same
--      account manages every product from one place.
--
--  Hardening (do these in the Supabase dashboard):
--    1. Authentication > Providers: DISABLE public sign-ups.
--    2. Create exactly ONE owner user.
--    3. Service-role key + signing private key stay server-side only.
-- ============================================================================

alter table products        enable row level security;
alter table modules         enable row level security;
alter table licenses        enable row level security;
alter table license_modules enable row level security;
alter table audit_log       enable row level security;

-- No policies for `anon` => anon is denied on every table.

-- Owner (authenticated) : full control from the admin panel, across all products.
drop policy if exists p_products_admin on products;
create policy p_products_admin on products
  for all to authenticated using (true) with check (true);

drop policy if exists p_modules_admin on modules;
create policy p_modules_admin on modules
  for all to authenticated using (true) with check (true);

drop policy if exists p_licenses_admin on licenses;
create policy p_licenses_admin on licenses
  for all to authenticated using (true) with check (true);

drop policy if exists p_license_modules_admin on license_modules;
create policy p_license_modules_admin on license_modules
  for all to authenticated using (true) with check (true);

-- Audit log : admin may READ; writes come from the service role / server-side
-- RPCs. Block direct client writes so history is tamper-evident.
drop policy if exists p_audit_read on audit_log;
create policy p_audit_read on audit_log
  for select to authenticated using (true);
