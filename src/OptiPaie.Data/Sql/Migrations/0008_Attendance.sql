-- ============================================================================
--  OptiPaie PRO - Migration 0008 : Attendance module (Gestion du pointage)
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. Attendance NEVER duplicates employee or company data: each
--  row references the shared Employees table by foreign key. Exactly one record
--  per (EmployeeId, WorkDate) is enforced by a unique index, so a day can never
--  be recorded twice.
--
--  Decimals are stored as invariant TEXT (matching the existing money/rate
--  convention and the Dapper decimal type handler); dates as ISO-8601.
-- ============================================================================

CREATE TABLE IF NOT EXISTS AttendanceRecords (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    EmployeeId    INTEGER NOT NULL,
    WorkDate      TEXT    NOT NULL,
    Status        INTEGER NOT NULL DEFAULT 1 CHECK (Status IN (1, 2, 3, 4, 5, 6)),
    CheckIn       TEXT,
    CheckOut      TEXT,
    WorkedHours   TEXT    NOT NULL DEFAULT '0',
    LateMinutes   INTEGER NOT NULL DEFAULT 0 CHECK (LateMinutes >= 0),
    OvertimeHours TEXT    NOT NULL DEFAULT '0',
    Notes         TEXT,
    CreatedAtUtc  TEXT    NOT NULL,
    UpdatedAtUtc  TEXT,
    IsDeleted     INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Attendance_Employees
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

-- One attendance day per employee (the anti-duplication rule).
CREATE UNIQUE INDEX IF NOT EXISTS UX_Attendance_Employee_Date
    ON AttendanceRecords (EmployeeId, WorkDate);

CREATE INDEX IF NOT EXISTS IX_Attendance_Date     ON AttendanceRecords (WorkDate);
CREATE INDEX IF NOT EXISTS IX_Attendance_Employee ON AttendanceRecords (EmployeeId);
