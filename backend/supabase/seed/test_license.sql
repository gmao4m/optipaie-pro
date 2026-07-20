-- ============================================================================
--  OptiPaie DZ — create ONE test license (for the Payroll product) so you can
--  try activate/validate before the Admin Panel (Phase 4) exists.
--  Run this in the Supabase SQL editor; it prints the generated key.
-- ============================================================================
do $$
declare
  payroll_id uuid;
  new_id     uuid;
  new_key    text;
begin
  select id into payroll_id from products where key = 'payroll';
  if payroll_id is null then
    raise exception 'Product "payroll" not found — run 0001_licensing_schema.sql first.';
  end if;

  new_key := gen_license_key((select key_prefix from products where id = payroll_id));

  insert into licenses (product_id, license_key, company_name, email, phone,
                        purchase_date, status, notes)
  values (payroll_id, new_key, 'Entreprise Test SARL', 'test@example.com',
          '+213 555 00 00 00', current_date, 'pending',
          'Licence de test générée par test_license.sql')
  returning id into new_id;

  -- Seed the module matrix for this license: payroll (core) + ATS on, rest off.
  insert into license_modules (license_id, product_id, module_key, enabled)
  select new_id, payroll_id, key, (key in ('payroll','ats'))
  from modules
  where product_id = payroll_id;

  insert into audit_log (admin_email, product_id, product_key, action,
                         license_id, license_key, company_name, details)
  values ('seed@local', payroll_id, 'payroll', 'license.generate',
          new_id, new_key, 'Entreprise Test SARL',
          jsonb_build_object('source', 'test_license.sql'));

  raise notice 'TEST LICENSE KEY = %  (product: payroll)', new_key;
end $$;

-- Show it again for convenience:
select l.license_key, p.key as product, l.company_name, l.status, l.created_at
from licenses l
join products p on p.id = l.product_id
order by l.created_at desc
limit 1;
