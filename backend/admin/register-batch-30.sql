-- ============================================================================
--  OptiPaie DZ - Register the 30 licenses printed in licenses-batch-30.pdf
-- ----------------------------------------------------------------------------
--  Run this ONCE in the Supabase SQL editor (after 0001/0002/0003). It inserts
--  these exact 30 keys for the Payroll product so the printed cards become VALID
--  and activatable. Idempotent: re-running will not duplicate licenses.
-- ============================================================================
do $$
declare
  v_pid uuid;
  v_id  uuid;
  k     text;
  keys  text[] := array[
    'PAY-G9FP-DMPZ-DJXS',
    'PAY-TEH3-QRSR-QG5U',
    'PAY-8DU4-NNLX-Y3X8',
    'PAY-LJ99-4PWF-2VGW',
    'PAY-5KJC-MG3E-YV46',
    'PAY-HMGM-NVTL-HD76',
    'PAY-UHN5-Y9ZX-KDY7',
    'PAY-UF23-ZZHQ-YFCU',
    'PAY-KFCD-APZM-8CL2',
    'PAY-ZJHT-PSTW-Z8GX',
    'PAY-RRJL-TC42-QVUW',
    'PAY-9AKB-DLGC-6GFR',
    'PAY-PMQ2-5JZZ-MGJL',
    'PAY-DKGG-MM83-WX39',
    'PAY-QKA6-SUB3-DQBT',
    'PAY-U48S-A8MF-CCNY',
    'PAY-NWN9-SRUY-C4C7',
    'PAY-HC33-HQJH-ZM6C',
    'PAY-QXAY-HYPP-EVWV',
    'PAY-EUVU-USDF-K2VE',
    'PAY-MRSL-C2S3-GBBJ',
    'PAY-QEFL-PQSB-9XJF',
    'PAY-3D7U-3VGJ-ZANE',
    'PAY-RDXR-V2YM-SAAH',
    'PAY-HU3A-C35V-N5Z6',
    'PAY-QBBM-J6M9-XZ8W',
    'PAY-AHD8-WR6P-JYMD',
    'PAY-SDSP-GDYM-9KV7',
    'PAY-HXJB-AP9K-RWJF',
    'PAY-92ZE-ACMA-EM52'
  ];
begin
  select id into v_pid from products where key = 'payroll';
  if v_pid is null then
    raise exception 'Product "payroll" not found - run 0001_licensing_schema.sql first.';
  end if;

  foreach k in array keys loop
    insert into licenses (product_id, license_key, company_name, status, notes)
    values (v_pid, k, '(non attribuée)', 'pending', 'Lot PDF 30 (licenses-batch-30.pdf)')
    on conflict (license_key) do nothing;

    select id into v_id from licenses where license_key = k;

    insert into license_modules (license_id, product_id, module_key, enabled)
    select v_id, v_pid, m.key, m.is_core
    from modules m
    where m.product_id = v_pid
    on conflict (license_id, module_key) do nothing;

    insert into audit_log (admin_email, product_id, product_key, action,
                           license_key, company_name, details)
    values ('admin', v_pid, 'payroll', 'license.generate', k, '(non attribuée)',
            jsonb_build_object('bulk', true, 'source', 'pdf-batch-30'));
  end loop;

  raise notice 'Registered % licenses.', array_length(keys, 1);
end $$;

select license_key, status from licenses
where notes = 'Lot PDF 30 (licenses-batch-30.pdf)'
order by license_key;
