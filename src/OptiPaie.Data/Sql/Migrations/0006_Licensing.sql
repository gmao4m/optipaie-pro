-- ============================================================================
--  OptiPaie DZ - Migration 0006 : local licensing cache
-- ----------------------------------------------------------------------------
--  Adds the offline license cache used by the new licensing system. Purely
--  ADDITIVE — it does not touch Employees, Companies, Payroll or any existing
--  table, so it is fully backward compatible and safe to apply on any existing
--  database.
--
--    LicenseState   : single-row (Id = 1) cache holding the signed license
--                     token plus a few denormalised fields for quick display.
--                     The signed token is the source of truth and is re-verified
--                     on every launch; LastSeenUtc is the anti clock-rollback guard.
--    LicenseModules : the cached list of enabled module keys (a mirror of the
--                     token's modules, for display without re-verifying).
--
--  Dates are stored as ISO-8601 UTC TEXT (invariant), matching the existing
--  date/decimal-as-TEXT convention used elsewhere in the schema.
-- ============================================================================

CREATE TABLE IF NOT EXISTS LicenseState (
    Id                INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    ProductKey        TEXT    NOT NULL,
    LicenseKey        TEXT,
    CompanyName       TEXT,
    Email             TEXT,
    DeviceId          TEXT,
    Status            TEXT,
    SignedToken       TEXT,
    ActivatedAtUtc    TEXT,
    LastValidationUtc TEXT,
    ExpiresAtUtc      TEXT,
    GraceUntilUtc     TEXT,
    LastSeenUtc       TEXT,
    UpdatedAtUtc      TEXT
);

CREATE TABLE IF NOT EXISTS LicenseModules (
    ModuleKey TEXT    NOT NULL PRIMARY KEY,
    Enabled   INTEGER NOT NULL DEFAULT 0 CHECK (Enabled IN (0, 1))
);
