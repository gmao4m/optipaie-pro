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
