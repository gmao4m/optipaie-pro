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
