-- ============================================================================
--  OptiPaie PRO - Migration 0018 : Work Certificates module (Attestations)
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. A certificate references the shared Employees table; only the
--  issue metadata is stored. The body is rendered live from the shared employee and
--  company records, so nothing is duplicated.
--
--  Dates are ISO-8601.
-- ============================================================================

CREATE TABLE IF NOT EXISTS WorkCertificates (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    EmployeeId   INTEGER NOT NULL,
    Type         INTEGER NOT NULL CHECK (Type IN (1, 2, 3, 99)),
    Reference    TEXT,
    IssueDate    TEXT    NOT NULL,
    Purpose      TEXT,
    Body         TEXT,
    CreatedAtUtc TEXT    NOT NULL,
    UpdatedAtUtc TEXT,
    IsDeleted    INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Certificate_Employees
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS IX_Certificate_Employee ON WorkCertificates (EmployeeId);
CREATE INDEX IF NOT EXISTS IX_Certificate_Issue    ON WorkCertificates (IssueDate);
