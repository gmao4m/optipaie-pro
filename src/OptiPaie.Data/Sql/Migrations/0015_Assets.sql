-- ============================================================================
--  OptiPaie PRO - Migration 0015 : Assets module (Gestion du matériel)
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. An asset belongs to a Company; it is handed to a SHARED
--  employee through an assignment row that references the Employees table. Neither
--  employee nor company data is copied.
--
--  Values are stored as invariant TEXT (existing money convention); dates as
--  ISO-8601.
-- ============================================================================

CREATE TABLE IF NOT EXISTS Assets (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId     INTEGER NOT NULL,
    Name          TEXT    NOT NULL,
    Category      INTEGER NOT NULL CHECK (Category IN (1, 2, 3, 4, 5, 99)),
    Status        INTEGER NOT NULL DEFAULT 1 CHECK (Status IN (1, 2, 3, 4)),
    SerialNumber  TEXT,
    PurchaseDate  TEXT,
    PurchaseValue TEXT    NOT NULL DEFAULT '0',
    Notes         TEXT,
    CreatedAtUtc  TEXT    NOT NULL,
    UpdatedAtUtc  TEXT,
    IsDeleted     INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Asset_Companies
        FOREIGN KEY (CompanyId) REFERENCES Companies (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS AssetAssignments (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    AssetId      INTEGER NOT NULL,
    EmployeeId   INTEGER NOT NULL,
    AssignedDate TEXT    NOT NULL,
    ReturnedDate TEXT,
    ConditionOut TEXT,
    ConditionIn  TEXT,
    Notes        TEXT,
    CreatedAtUtc TEXT    NOT NULL,
    IsDeleted    INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Assignment_Assets
        FOREIGN KEY (AssetId) REFERENCES Assets (Id)
        ON UPDATE CASCADE ON DELETE CASCADE,
    CONSTRAINT FK_Assignment_Employees
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

-- At most one open (not-yet-returned) assignment per asset.
CREATE UNIQUE INDEX IF NOT EXISTS UX_Assignment_OpenPerAsset
    ON AssetAssignments (AssetId)
    WHERE ReturnedDate IS NULL AND IsDeleted = 0;

CREATE INDEX IF NOT EXISTS IX_Asset_Company        ON Assets (CompanyId);
CREATE INDEX IF NOT EXISTS IX_Assignment_Asset     ON AssetAssignments (AssetId);
CREATE INDEX IF NOT EXISTS IX_Assignment_Employee  ON AssetAssignments (EmployeeId);
