-- ============================================================================
--  OptiPaie PRO - Migration 0020 : Attendance "Mission" status (7)
-- ----------------------------------------------------------------------------
--  Widens the AttendanceRecords.Status CHECK to allow status 7 (Mission —
--  business trip, worked and paid). SQLite cannot alter a CHECK in place, so the
--  table is rebuilt: create a copy with the wider constraint, move the data, drop
--  the old table, rename, and recreate the indexes (incl. the partial unique index
--  from migration 0009). No other table references AttendanceRecords, so this is
--  safe inside the migration transaction. Data is preserved byte-for-byte.
-- ============================================================================

CREATE TABLE AttendanceRecords_new (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    EmployeeId    INTEGER NOT NULL,
    WorkDate      TEXT    NOT NULL,
    Status        INTEGER NOT NULL DEFAULT 1 CHECK (Status IN (1, 2, 3, 4, 5, 6, 7)),
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

INSERT INTO AttendanceRecords_new
    (Id, EmployeeId, WorkDate, Status, CheckIn, CheckOut, WorkedHours, LateMinutes,
     OvertimeHours, Notes, CreatedAtUtc, UpdatedAtUtc, IsDeleted)
SELECT
     Id, EmployeeId, WorkDate, Status, CheckIn, CheckOut, WorkedHours, LateMinutes,
     OvertimeHours, Notes, CreatedAtUtc, UpdatedAtUtc, IsDeleted
FROM AttendanceRecords;

DROP TABLE AttendanceRecords;
ALTER TABLE AttendanceRecords_new RENAME TO AttendanceRecords;

CREATE UNIQUE INDEX IF NOT EXISTS UX_Attendance_Employee_Date
    ON AttendanceRecords (EmployeeId, WorkDate)
    WHERE IsDeleted = 0;

CREATE INDEX IF NOT EXISTS IX_Attendance_Date     ON AttendanceRecords (WorkDate);
CREATE INDEX IF NOT EXISTS IX_Attendance_Employee ON AttendanceRecords (EmployeeId);
