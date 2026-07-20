-- OptiPaie PRO - COMPLETE database setup (run ONCE in Supabase SQL editor). Idempotent.

-- ===== migrations/0001_licensing_schema.sql =====
-- ============================================================================
--  OptiPaie DZ — Centralized Licensing Platform : schema (Supabase / PostgreSQL)
-- ----------------------------------------------------------------------------
--  A MULTI-PRODUCT licensing backend. One cloud, one auth, one audit log, one
--  licensing engine — serving many desktop products (Payroll today; Fleet,
--  Accounting, ATS, Construction, … later) with NO future redesign.
--
--  Everything is scoped by PRODUCT:
--    products         — each desktop software product (key, version, rules)
--    modules          — the sellable/core modules OF A PRODUCT
--    licenses         — one row per customer license (belongs to a product)
--    license_modules  — the per-license module on/off matrix
--    audit_log        — complete history across all products
--
--  Stores LICENSE METADATA ONLY — never employees, payroll, salaries or any HR
--  business data. Each desktop app talks only to the licensing API and receives
--  permissions for ITS OWN product; apps never know about each other.
--
--  Idempotent-ish and additive; safe to run once on a fresh project.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- Helper: keep updated_at fresh on every UPDATE
-- ----------------------------------------------------------------------------
create or replace function set_updated_at()
returns trigger as $$
begin
  new.updated_at = now();
  return new;
end;
$$ language plpgsql;

-- ----------------------------------------------------------------------------
-- Helper: generate a customer-friendly license key  ->  PAY-8X2Q-7A9L-5D3K
--   The prefix is per-product (PAY, FLT, ACC, …). Charset excludes ambiguous
--   characters (0/O, 1/I) to avoid support pain.
-- ----------------------------------------------------------------------------
create or replace function gen_license_key(prefix text default 'PAY')
returns text as $$
declare
  charset text := 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  result  text := upper(coalesce(nullif(trim(prefix), ''), 'LIC'));
  part    text;
  i int;
  j int;
begin
  for i in 1..3 loop
    part := '';
    for j in 1..4 loop
      part := part || substr(charset, floor(random() * length(charset))::int + 1, 1);
    end loop;
    result := result || '-' || part;
  end loop;
  return result;
end;
$$ language plpgsql volatile;

-- ----------------------------------------------------------------------------
-- products : each desktop software product managed by the platform
--   activation_rules (jsonb) lets every product define its OWN rules without a
--   schema change, e.g. { "graceDays":30, "bindDevice":true, "maxDevices":1 }.
-- ----------------------------------------------------------------------------
create table if not exists products (
  id               uuid primary key default gen_random_uuid(),
  key              text unique not null,            -- 'payroll', 'fleet', 'accounting', ...
  name             text not null,
  key_prefix       text not null default 'LIC',     -- license-key prefix for this product
  current_version  text,                            -- latest released version
  activation_rules jsonb not null default
                     '{"graceDays":30,"bindDevice":true,"maxDevices":1}'::jsonb,
  created_at       timestamptz not null default now()
);

-- ----------------------------------------------------------------------------
-- modules : catalogue shown as the admin on/off matrix, SCOPED per product
-- ----------------------------------------------------------------------------
create table if not exists modules (
  product_id  uuid not null references products (id) on delete cascade,
  key         text not null,                        -- 'payroll','ats','attendance',...
  name_fr     text not null,
  name_ar     text not null,
  sort_order  int  not null default 0,
  is_core     boolean not null default false,       -- core = always enabled (base product)
  primary key (product_id, key)
);

-- ----------------------------------------------------------------------------
-- licenses : one per customer (of a given product)
-- ----------------------------------------------------------------------------
create table if not exists licenses (
  id                 uuid primary key default gen_random_uuid(),
  product_id         uuid not null references products (id),
  license_key        text unique not null,          -- globally unique across products
  company_name       text not null,
  company_id         text,                           -- your internal customer reference
  email              text,
  phone              text,
  purchase_date      date,
  notes              text,                            -- owner-internal notes only
  device_id          text,                            -- bound on first activation; null = free
  device_info        text,                            -- optional human-readable device details
  app_version        text,                            -- version the desktop last reported
  status             text not null default 'pending'
                       check (status in ('pending','active','suspended','revoked')),
  activated_at       timestamptz,
  last_validation_at timestamptz,
  expires_at         timestamptz,                     -- null = perpetual
  created_at         timestamptz not null default now(),
  updated_at         timestamptz not null default now()
);

drop trigger if exists trg_licenses_updated on licenses;
create trigger trg_licenses_updated
  before update on licenses
  for each row execute function set_updated_at();

create index if not exists ix_licenses_product on licenses (product_id);
create index if not exists ix_licenses_key     on licenses (license_key);
create index if not exists ix_licenses_status  on licenses (status);
create index if not exists ix_licenses_device  on licenses (device_id);
create index if not exists ix_licenses_search  on licenses
  using gin (to_tsvector('simple',
    coalesce(company_name,'') || ' ' || coalesce(email,'') || ' ' ||
    coalesce(phone,'') || ' ' || coalesce(license_key,'')));

-- ----------------------------------------------------------------------------
-- license_modules : which modules are enabled for each license
--   product_id is carried so the FK guarantees the module belongs to the same
--   product as the license (admin/seed logic keeps it = the license's product).
-- ----------------------------------------------------------------------------
create table if not exists license_modules (
  license_id  uuid not null references licenses (id) on delete cascade,
  product_id  uuid not null,
  module_key  text not null,
  enabled     boolean not null default false,
  primary key (license_id, module_key),
  foreign key (product_id, module_key) references modules (product_id, key)
);

create index if not exists ix_license_modules_license on license_modules (license_id);

-- ----------------------------------------------------------------------------
-- audit_log : complete history across ALL products
--   action examples:
--     license.generate | license.activate | license.suspend | license.reactivate
--     license.delete   | module.enable    | module.disable   | device.reset
--     license.validate | activation.error | validation.error
-- ----------------------------------------------------------------------------
create table if not exists audit_log (
  id           bigint generated always as identity primary key,
  admin_email  text,                    -- null for automated customer events
  product_id   uuid,
  product_key  text,
  action       text not null,
  license_id   uuid,                    -- soft link (kept even if license later deleted)
  license_key  text,
  company_name text,
  details      jsonb,
  created_at   timestamptz not null default now()
);

create index if not exists ix_audit_created on audit_log (created_at desc);
create index if not exists ix_audit_product on audit_log (product_id);
create index if not exists ix_audit_license on audit_log (license_id);

-- ============================================================================
--  Seed : the FIRST product (Payroll) and its module catalogue.
--    payroll is the CORE base product (always enabled when active); the rest
--    are purchasable upsells. companies/employees/archive/settings are implicit
--    core screens handled by the desktop registry and are intentionally NOT
--    license-controlled here.
--    Adding a future product later = one INSERT into products + its modules.
-- ============================================================================
insert into products (key, name, key_prefix, current_version)
values ('payroll', 'OptiPaie DZ — Paie', 'PAY', '1.0.0')
on conflict (key) do update
  set name       = excluded.name,
      key_prefix = excluded.key_prefix;

--  CANONICAL module registry for the Payroll product. These keys are the single
--  source of truth — the desktop app's IModuleRegistry (Phase 2) reuses them
--  verbatim. payroll is core (always enabled); every upsell is disabled by
--  default (a new license simply has no enabling row).
insert into modules (product_id, key, name_fr, name_ar, sort_order, is_core)
select p.id, m.key, m.name_fr, m.name_ar, m.sort_order, m.is_core
from products p
cross join (values
  ('payroll',          'Paie',                        'الأجور',                 10, true),
  ('ats',              'ATS / DRT',                   'ATS / DRT',              20, false),
  ('attendance',       'Gestion du pointage',         'إدارة الحضور',           30, false),
  ('leave',            'Gestion des congés',          'إدارة العطل',            40, false),
  ('loans',            'Prêts & avances',             'القروض والتسبيقات',      50, false),
  ('performance',      'Évaluation & promotions',     'التقييم والترقيات',      60, false),
  ('contracts',        'Contrats & renouvellements',  'العقود والتجديد',        70, false),
  ('training',         'Formation & cours',           'التكوين والدورات',       80, false),
  ('assets',           'Biens & équipements',         'الأصول والمعدات',        90, false),
  ('work_certificate', 'Attestation de travail',      'شهادة العمل',           100, false)
) as m(key, name_fr, name_ar, sort_order, is_core)
where p.key = 'payroll'
on conflict (product_id, key) do update
  set name_fr    = excluded.name_fr,
      name_ar    = excluded.name_ar,
      sort_order = excluded.sort_order,
      is_core    = excluded.is_core;


-- ===== migrations/0002_rls_policies.sql =====
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


-- ===== migrations/0003_admin_views.sql =====
-- ============================================================================
--  OptiPaie DZ — Licensing platform : admin dashboard views
-- ----------------------------------------------------------------------------
--  Read-only views that back the Admin Panel dashboard (Phase 4). Every metric
--  you asked for is scoped per product, so the panel can switch products and
--  show the right numbers. Only the authenticated owner can read them.
-- ============================================================================

-- Per-product headline metrics:
--   total_customers, total_licenses, active, suspended, revoked, pending,
--   new_this_month.
create or replace view v_product_stats as
select
  p.id   as product_id,
  p.key  as product_key,
  p.name as product_name,
  count(l.id)                                             as total_licenses,
  count(l.id)                                             as total_customers,
  count(l.id) filter (where l.status = 'active')         as active_licenses,
  count(l.id) filter (where l.status = 'suspended')      as suspended_licenses,
  count(l.id) filter (where l.status = 'revoked')        as revoked_licenses,
  count(l.id) filter (where l.status = 'pending')        as pending_licenses,
  count(l.id) filter (
    where l.created_at >= date_trunc('month', now())
  )                                                       as new_customers_this_month
from products p
left join licenses l on l.product_id = p.id
group by p.id, p.key, p.name;

-- Module activation statistics: for each product+module, how many licenses have
-- it enabled (and the module display names for the chart labels).
create or replace view v_module_activation_stats as
select
  m.product_id,
  p.key                                          as product_key,
  m.key                                          as module_key,
  m.name_fr,
  m.name_ar,
  m.sort_order,
  m.is_core,
  count(lm.license_id) filter (where lm.enabled) as enabled_count
from modules m
join products p on p.id = m.product_id
left join license_modules lm
       on lm.product_id = m.product_id and lm.module_key = m.key
group by m.product_id, p.key, m.key, m.name_fr, m.name_ar, m.sort_order, m.is_core;

-- Expose to the owner account only (never anon).
grant select on v_product_stats            to authenticated;
grant select on v_module_activation_stats  to authenticated;


-- ===== migrations/0004_admin_functions.sql =====
-- ============================================================================
--  OptiPaie DZ — Licensing platform : admin server-side functions
-- ----------------------------------------------------------------------------
--  generate_licenses(): mint N unique license keys for a product in one call.
--  Each new license is created as a "pool" license (status = pending, company
--  name = placeholder until a customer activates it), its module matrix is
--  seeded (core on, upsells off), and a bulk 'license.generate' audit row is
--  written per key. Returns the generated keys so they can be printed.
--
--  Runs as SECURITY DEFINER so it works from the authenticated admin (and later
--  the admin panel via RPC). Key format stays PAY-XXXX-XXXX-XXXX (15 alphanumerics).
-- ============================================================================

create or replace function generate_licenses(
  p_count       int,
  p_product_key text default 'payroll',
  p_notes       text default null,
  p_admin_email text default 'admin'
)
returns table (license_key text)
language plpgsql
security definer
set search_path = public
as $$
declare
  v_product_id uuid;
  v_prefix     text;
  v_key        text;
  i            int;
begin
  if p_count is null or p_count < 1 or p_count > 1000 then
    raise exception 'Count must be between 1 and 1000 (got %).', p_count;
  end if;

  select id, key_prefix into v_product_id, v_prefix
  from products where key = p_product_key;

  if v_product_id is null then
    raise exception 'Unknown product "%".', p_product_key;
  end if;

  for i in 1..p_count loop
    -- Draw a key that is not already taken (retries on the rare collision).
    loop
      v_key := gen_license_key(v_prefix);
      exit when not exists (select 1 from licenses l where l.license_key = v_key);
    end loop;

    insert into licenses (product_id, license_key, company_name, status, notes)
    values (v_product_id, v_key, '(non attribuée)', 'pending', p_notes);

    -- Seed the on/off matrix: core enabled, upsells disabled.
    insert into license_modules (license_id, product_id, module_key, enabled)
    select l.id, v_product_id, m.key, m.is_core
    from licenses l
    join modules m on m.product_id = v_product_id
    where l.license_key = v_key;

    insert into audit_log (admin_email, product_id, product_key, action,
                           license_key, company_name, details)
    values (p_admin_email, v_product_id, p_product_key, 'license.generate',
            v_key, '(non attribuée)', jsonb_build_object('bulk', true, 'batch_size', p_count));

    license_key := v_key;
    return next;
  end loop;
end;
$$;

-- Only the authenticated owner (or the SQL editor / service role) may generate.
revoke all on function generate_licenses(int, text, text, text) from public, anon;
grant execute on function generate_licenses(int, text, text, text) to authenticated;


-- ===== migrations/0005_enterprise.sql =====
-- ============================================================================
--  OptiPaie PRO - Enterprise licensing schema (customers, devices, activations,
--  license_logs) + license types + multi-device + new key format.
--  Additive and non-destructive; safe to run on the existing project after 0001-0004.
-- ============================================================================

-- ---- customers -------------------------------------------------------------
create table if not exists customers (
  id         uuid primary key default gen_random_uuid(),
  name       text not null,
  email      text,
  phone      text,
  notes      text,
  created_at timestamptz not null default now()
);

-- ---- licenses: enterprise columns -----------------------------------------
alter table licenses add column if not exists customer_id uuid references customers (id);
alter table licenses add column if not exists type        text not null default 'lifetime';
alter table licenses add column if not exists max_devices int  not null default 1;

do $$ begin
  if not exists (select 1 from pg_constraint where conname = 'licenses_type_check') then
    alter table licenses add constraint licenses_type_check
      check (type in ('trial','lifetime','annual','monthly','demo','enterprise'));
  end if;
end $$;

-- ---- devices : every machine a license is activated on ---------------------
create table if not exists devices (
  id           uuid primary key default gen_random_uuid(),
  license_id   uuid not null references licenses (id) on delete cascade,
  device_id    text not null,
  device_info  text,
  app_version  text,
  activated_at timestamptz not null default now(),
  last_seen_at timestamptz not null default now(),
  is_active    boolean not null default true,
  unique (license_id, device_id)
);
create index if not exists ix_devices_license on devices (license_id);

-- ---- activations : append-only activate/validate event log -----------------
create table if not exists activations (
  id          bigint generated always as identity primary key,
  license_id  uuid references licenses (id) on delete set null,
  device_id   text,
  action      text not null,          -- activate | validate | deactivate
  result      text,                   -- ok | invalid_key | max_devices | wrong_product | ...
  app_version text,
  created_at  timestamptz not null default now()
);
create index if not exists ix_activations_license on activations (license_id);

-- ---- license_logs : unified per-license history (audit + activations) ------
create or replace view license_logs as
  select id::text                       as id,
         license_id,
         license_key,
         action,
         details,
         admin_email                    as actor,
         'audit'::text                  as source,
         created_at
  from audit_log
  union all
  select 'act-' || id::text,
         license_id,
         null::text,
         action,
         jsonb_build_object('result', result, 'device_id', device_id, 'app_version', app_version),
         device_id,
         'activation'::text,
         created_at
  from activations;

grant select on license_logs to authenticated;

alter table customers   enable row level security;
alter table devices     enable row level security;
alter table activations enable row level security;

drop policy if exists p_customers_admin on customers;
create policy p_customers_admin on customers for all to authenticated using (true) with check (true);
drop policy if exists p_devices_admin on devices;
create policy p_devices_admin on devices for all to authenticated using (true) with check (true);
drop policy if exists p_activations_read on activations;
create policy p_activations_read on activations for select to authenticated using (true);

-- ---- new key format: XXXXX-XXXXX-XXXXX-XXXXX (4 groups of 5) ----------------
--  Signature kept compatible with generate_licenses(int, text, ...); the prefix
--  argument is now ignored (the enterprise format has no product prefix).
create or replace function gen_license_key(prefix text default null)
returns text as $$
declare
  charset text := 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  result  text := '';
  part    text;
  i int;
  j int;
begin
  for i in 1..4 loop
    part := '';
    for j in 1..5 loop
      part := part || substr(charset, floor(random() * length(charset))::int + 1, 1);
    end loop;
    result := case when i = 1 then part else result || '-' || part end;
  end loop;
  return result;
end;
$$ language plpgsql volatile;


-- ===== migrations/0006_admin_platform.sql =====
-- ============================================================================
--  OptiPaie PRO - Commercial admin platform: activation keys, updates,
--  app_settings, module_permissions view + admin overview. Additive, lightweight.
-- ============================================================================

-- ---- app_settings : small key/value store (min version, latest version, …) --
create table if not exists app_settings (
  key        text primary key,
  value      text,
  updated_at timestamptz not null default now()
);

-- ---- updates : auto-update metadata (Velopack feed) ------------------------
create table if not exists updates (
  id            uuid primary key default gen_random_uuid(),
  version       text not null,
  channel       text not null default 'stable',
  mandatory     boolean not null default false,
  release_notes text,
  package_url   text,          -- full package / Velopack feed location
  delta_url     text,          -- optional delta package
  checksum      text,
  is_latest     boolean not null default false,
  published_at  timestamptz not null default now()
);
create unique index if not exists ux_updates_version on updates (version, channel);

-- ---- activation_keys : single-use, per-module unlock keys ------------------
create table if not exists activation_keys (
  id          uuid primary key default gen_random_uuid(),
  license_id  uuid not null references licenses (id) on delete cascade,
  module_key  text not null,
  key_code    text unique not null,
  status      text not null default 'unused'
                check (status in ('unused','used','revoked','expired')),
  created_at  timestamptz not null default now(),
  expires_at  timestamptz,
  used_at     timestamptz,
  used_device text
);
create index if not exists ix_activation_keys_license on activation_keys (license_id);
create index if not exists ix_activation_keys_status  on activation_keys (status);

-- optional per-license module expiration + activation date
alter table license_modules add column if not exists expires_at   timestamptz;
alter table license_modules add column if not exists activated_at timestamptz;

-- ---- module_permissions : friendly view over license_modules ---------------
create or replace view module_permissions as
  select lm.license_id, lm.product_id, lm.module_key, lm.enabled, lm.expires_at,
         m.name_fr, m.name_ar, m.is_core, m.sort_order
  from license_modules lm
  join modules m on m.product_id = lm.product_id and m.key = lm.module_key;

-- ---- v_admin_overview : dashboard headline counts --------------------------
create or replace view v_admin_overview as
  select
    (select count(*) from licenses)                                             as total_licenses,
    (select count(*) from licenses where status = 'active')                     as active_licenses,
    (select count(*) from licenses where status in ('suspended','revoked'))     as disabled_licenses,
    (select count(*) from licenses where expires_at is not null and expires_at < now()) as expired_licenses,
    (select count(*) from devices  where is_active)                             as active_devices,
    (select count(*) from licenses where created_at >= date_trunc('month', now())) as new_this_month;

grant select on module_permissions to authenticated;
grant select on v_admin_overview   to authenticated;

-- ---- RLS -------------------------------------------------------------------
alter table app_settings    enable row level security;
alter table updates         enable row level security;
alter table activation_keys enable row level security;

-- updates + app_settings: public READ (the desktop checks for updates with the
-- publishable key); admin writes require the authenticated owner.
drop policy if exists p_updates_read on updates;
create policy p_updates_read on updates for select to anon, authenticated using (true);
drop policy if exists p_updates_admin on updates;
create policy p_updates_admin on updates for all to authenticated using (true) with check (true);

drop policy if exists p_app_settings_read on app_settings;
create policy p_app_settings_read on app_settings for select to anon, authenticated using (true);
drop policy if exists p_app_settings_admin on app_settings;
create policy p_app_settings_admin on app_settings for all to authenticated using (true) with check (true);

-- activation_keys: admin only (the desktop validates a key via an Edge Function).
drop policy if exists p_activation_keys_admin on activation_keys;
create policy p_activation_keys_admin on activation_keys for all to authenticated using (true) with check (true);

-- ---- generate_module_keys : mint single-use module activation keys ---------
create or replace function generate_module_keys(
  p_license_key text,
  p_module_key  text,
  p_count       int,
  p_expires     timestamptz default null
)
returns table (key_code text)
language plpgsql
security definer
set search_path = public
as $$
declare
  v_license_id uuid;
  v_code       text;
  i int;
begin
  select id into v_license_id from licenses where license_key = p_license_key;
  if v_license_id is null then
    raise exception 'Unknown license "%".', p_license_key;
  end if;
  if p_count is null or p_count < 1 or p_count > 1000 then
    raise exception 'Count must be between 1 and 1000.';
  end if;

  for i in 1..p_count loop
    loop
      v_code := 'MOD-' || gen_license_key();
      exit when not exists (select 1 from activation_keys a where a.key_code = v_code);
    end loop;

    insert into activation_keys (license_id, module_key, key_code, expires_at)
    values (v_license_id, p_module_key, v_code, p_expires);

    insert into audit_log (admin_email, action, license_id, license_key, details)
    values ('admin', 'module_key.generate', v_license_id, p_license_key,
            jsonb_build_object('module', p_module_key, 'key_code', v_code, 'expires_at', p_expires));

    key_code := v_code;
    return next;
  end loop;
end;
$$;

revoke all on function generate_module_keys(text, text, int, timestamptz) from public, anon;
grant execute on function generate_module_keys(text, text, int, timestamptz) to authenticated;

-- ---- revoke_activation_key : revoke a single key (audited) -----------------
create or replace function revoke_activation_key(p_key_id uuid)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
  v_row activation_keys%rowtype;
begin
  update activation_keys set status = 'revoked'
  where id = p_key_id and status in ('unused','used')
  returning * into v_row;

  if found then
    insert into audit_log (admin_email, action, license_id, details)
    values ('admin', 'module_key.revoke', v_row.license_id,
            jsonb_build_object('module', v_row.module_key, 'key_code', v_row.key_code));
  end if;
end;
$$;

revoke all on function revoke_activation_key(uuid) from public, anon;
grant execute on function revoke_activation_key(uuid) to authenticated;

insert into app_settings (key, value) values ('latest_version', '1.0.0')
on conflict (key) do nothing;

