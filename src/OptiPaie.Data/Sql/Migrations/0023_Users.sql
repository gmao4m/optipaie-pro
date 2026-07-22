-- ============================================================================
--  OptiPaie PRO - Migration 0023 : local user accounts & roles (opt-in)
-- ----------------------------------------------------------------------------
--  Additive. Adds optional local login: usernames with PBKDF2-hashed passwords
--  and a role (Admin / Manager). Purely dormant until an admin creates a user AND
--  enables the login gate — a fresh/demo install has no users, so the app keeps
--  running open exactly as before. Nothing here touches payroll.
-- ============================================================================

CREATE TABLE IF NOT EXISTS Users (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Username     TEXT    NOT NULL,
    FullName     TEXT,
    PasswordHash TEXT    NOT NULL,
    Salt         TEXT    NOT NULL,
    Role         INTEGER NOT NULL DEFAULT 2,          -- UserRole (1 = Admin, 2 = Manager)
    Department   TEXT,                                -- the team a Manager is scoped to
    IsActive     INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1)),
    CreatedAtUtc TEXT    NOT NULL,
    UpdatedAtUtc TEXT,
    IsDeleted    INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1))
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_Users_Username ON Users (Username) WHERE IsDeleted = 0;
