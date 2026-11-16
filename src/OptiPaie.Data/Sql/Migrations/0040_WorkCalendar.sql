-- ============================================================================
--  OptiPaie PRO - Migration 0040 : per-company work calendar
-- ----------------------------------------------------------------------------
--  PURELY ADDITIVE. One row per company describing the weekly work pattern, the
--  half-day definition, the rate for work on a rest day/holiday, and the
--  proration basis for a partial month. Holidays are NOT here — they reuse the
--  existing Holidays calendar (migration 0032).
--
--  EVERY default reproduces today's behaviour, so an existing company is
--  unchanged until it edits the screen:
--    - weekly pattern = Algerian Sun–Thu work, Fri–Sat rest (matches the
--      attendance screen's current hard-coded weekend),
--    - RestDayRate = 1 (no premium, as today),
--    - ProrationBasis = 0 (MonthDays — base ÷ the month's real days, today's rule).
--  No per-company row is seeded: a missing row means "defaults", and the
--  service returns the defaults, so nothing a company already set is overwritten.
-- ============================================================================

CREATE TABLE IF NOT EXISTS CompanyWorkCalendar (
    CompanyId      INTEGER NOT NULL PRIMARY KEY,
    WorkMon        INTEGER NOT NULL DEFAULT 1 CHECK (WorkMon IN (0, 1)),
    WorkTue        INTEGER NOT NULL DEFAULT 1 CHECK (WorkTue IN (0, 1)),
    WorkWed        INTEGER NOT NULL DEFAULT 1 CHECK (WorkWed IN (0, 1)),
    WorkThu        INTEGER NOT NULL DEFAULT 1 CHECK (WorkThu IN (0, 1)),
    WorkFri        INTEGER NOT NULL DEFAULT 0 CHECK (WorkFri IN (0, 1)),
    WorkSat        INTEGER NOT NULL DEFAULT 0 CHECK (WorkSat IN (0, 1)),
    WorkSun        INTEGER NOT NULL DEFAULT 1 CHECK (WorkSun IN (0, 1)),
    HalfDayHours   TEXT    NOT NULL DEFAULT '4',
    RestDayRate    TEXT    NOT NULL DEFAULT '1',
    ProrationBasis INTEGER NOT NULL DEFAULT 0 CHECK (ProrationBasis IN (0, 1, 2)),
    CreatedAtUtc   TEXT    NOT NULL,
    UpdatedAtUtc   TEXT
);
