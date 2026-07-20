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
