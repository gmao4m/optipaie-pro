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
