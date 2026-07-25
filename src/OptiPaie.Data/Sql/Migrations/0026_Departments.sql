-- Managed per-company departments. The employee's Department (a name string) is unchanged
-- and keeps driving Attendance grouping, reports, etc.; this table is the curated list the
-- employee dropdown and the evaluation module use.
CREATE TABLE IF NOT EXISTS Departments (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    CompanyId     INTEGER NOT NULL,
    Name          TEXT    NOT NULL,
    DisplayOrder  INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc  TEXT    NOT NULL,
    UpdatedAtUtc  TEXT    NULL,
    IsDeleted     INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (CompanyId) REFERENCES Companies(Id)
);

CREATE INDEX IF NOT EXISTS IX_Departments_Company ON Departments (CompanyId) WHERE IsDeleted = 0;
