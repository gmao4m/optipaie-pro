-- ============================================================================
--  OptiPaie PRO - Migration 0032 : public-holidays calendar (Jours fériés)
-- ----------------------------------------------------------------------------
--  PURELY ADDITIVE. Stores legal public holidays so a holiday falling inside a
--  leave period is NOT counted against the balance. CompanyId NULL = national.
--
--  Civil holidays have fixed dates (1 Jan, 12 Jan Yennayer, 1 May, 5 Jul,
--  1 Nov) and are always excluded by the service even before any row exists
--  (safety net), and can be pre-filled per year from the screen. Religious
--  holidays move each year and are entered per year (IsReligious = 1).
--
--  Empty on every existing database → no behaviour change until the user (or the
--  "pre-fill civil holidays" action) adds dates; the fixed civil dates are handled
--  in code, so they are excluded from counts regardless.
-- ============================================================================

CREATE TABLE IF NOT EXISTS Holidays (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId    INTEGER,                 -- NULL = national (all companies)
    HolidayDate  TEXT    NOT NULL,        -- ISO date
    NameAr       TEXT    NOT NULL,
    IsReligious  INTEGER NOT NULL DEFAULT 0 CHECK (IsReligious IN (0, 1)),
    CreatedAtUtc TEXT    NOT NULL,
    IsDeleted    INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1))
);

CREATE INDEX IF NOT EXISTS IX_Holidays_Date    ON Holidays (HolidayDate);
CREATE INDEX IF NOT EXISTS IX_Holidays_Company ON Holidays (CompanyId);
