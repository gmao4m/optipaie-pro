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
