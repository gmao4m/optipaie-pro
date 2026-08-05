-- 0007_company_scope.sql
-- Adds the company-scope (mono/multi-société) axis to licenses.
--
-- max_companies = the number of companies the desktop app may create for this
-- license:
--   1     -> Mono-société  (a single company)
--   0     -> Multi-sociétés (unlimited)
--
-- Default is 1 so that (a) every pre-existing license becomes Mono, and (b) an
-- absent/forgotten value is the SAFE, revenue-protecting choice on BOTH ends: the
-- server coerces a missing column to 1, and the desktop client reads a missing/null
-- token field as 1 (Mono) too. The admin tool writes 0 explicitly to grant Multi.
--
-- This axis is orthogonal to `type`/`expires_at` (the DURATION axis: annual vs
-- lifetime), so a license can be e.g. "Annual + Mono" or "Lifetime + Multi".
-- Idempotent — safe to re-run.

alter table licenses add column if not exists max_companies int not null default 1;

comment on column licenses.max_companies is
  'Companies the desktop may create: 1 = Mono-société, 0 = Multi-sociétés (unlimited). Missing/null is read as Mono.';
