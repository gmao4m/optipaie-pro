-- ============================================================================
--  OptiPaie PRO - Migration 0010 : Leave module (Gestion des congés)
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. A leave request references the shared Employees table by
--  foreign key; neither employee nor company data is copied. Approved requests
--  are mirrored into AttendanceRecords by the service, so payroll needs no
--  import step.
--
--  Decimals are stored as invariant TEXT (existing money/rate convention);
--  dates as ISO-8601.
-- ============================================================================

CREATE TABLE IF NOT EXISTS LeaveRequests (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    EmployeeId   INTEGER NOT NULL,
    Type         INTEGER NOT NULL CHECK (Type IN (1, 2, 3, 4, 5)),
    Status       INTEGER NOT NULL DEFAULT 1 CHECK (Status IN (1, 2, 3, 4)),
    StartDate    TEXT    NOT NULL,
    EndDate      TEXT    NOT NULL,
    Days         TEXT    NOT NULL DEFAULT '0',
    Reason       TEXT,
    DecisionNote TEXT,
    DecidedAtUtc TEXT,
    CreatedAtUtc TEXT    NOT NULL,
    UpdatedAtUtc TEXT,
    IsDeleted    INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT CK_Leave_Range CHECK (EndDate >= StartDate),
    CONSTRAINT FK_Leave_Employees
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS IX_Leave_Employee ON LeaveRequests (EmployeeId);
CREATE INDEX IF NOT EXISTS IX_Leave_Dates    ON LeaveRequests (StartDate, EndDate);
CREATE INDEX IF NOT EXISTS IX_Leave_Status   ON LeaveRequests (Status);
