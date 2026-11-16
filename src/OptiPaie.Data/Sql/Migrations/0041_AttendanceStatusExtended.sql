-- ============================================================================
--  OptiPaie PRO - Migration 0041 : extend AttendanceRecords.Status to 8, 9, 10
-- ----------------------------------------------------------------------------
--  Additive. Widens the Status CHECK to allow the exception statuses the clean
--  pointage needs:
--      8 = Justified absence (does NOT reduce pay)
--      9 = Half day          (reduces pay by 0.5)
--     10 = Worked a rest day / holiday (paid, may carry the calendar rate)
--  SQLite cannot alter a CHECK in place, so the table is rebuilt (same pattern as
--  migration 0020), carrying EVERY current column — including RowVersion (added by
--  0037) — and copying the data byte-for-byte. No other table references
--  AttendanceRecords, so this is safe inside the migration transaction. Existing
--  rows and their values are preserved exactly; the only change is the wider CHECK.
-- ============================================================================

CREATE TABLE AttendanceRecords_new (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    EmployeeId    INTEGER NOT NULL,
    WorkDate      TEXT    NOT NULL,
    Status        INTEGER NOT NULL DEFAULT 1 CHECK (Status IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)),
    CheckIn       TEXT,
    CheckOut      TEXT,
    WorkedHours   TEXT    NOT NULL DEFAULT '0',
    LateMinutes   INTEGER NOT NULL DEFAULT 0 CHECK (LateMinutes >= 0),
    OvertimeHours TEXT    NOT NULL DEFAULT '0',
    Notes         TEXT,
    CreatedAtUtc  TEXT    NOT NULL,
    UpdatedAtUtc  TEXT,
    IsDeleted     INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    RowVersion    INTEGER NOT NULL DEFAULT 0,
    CONSTRAINT FK_Attendance_Employees
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

INSERT INTO AttendanceRecords_new
    (Id, EmployeeId, WorkDate, Status, CheckIn, CheckOut, WorkedHours, LateMinutes,
     OvertimeHours, Notes, CreatedAtUtc, UpdatedAtUtc, IsDeleted, RowVersion)
SELECT
     Id, EmployeeId, WorkDate, Status, CheckIn, CheckOut, WorkedHours, LateMinutes,
     OvertimeHours, Notes, CreatedAtUtc, UpdatedAtUtc, IsDeleted, RowVersion
FROM AttendanceRecords;

DROP TABLE AttendanceRecords;
ALTER TABLE AttendanceRecords_new RENAME TO AttendanceRecords;

CREATE UNIQUE INDEX IF NOT EXISTS UX_Attendance_Employee_Date
    ON AttendanceRecords (EmployeeId, WorkDate)
    WHERE IsDeleted = 0;

CREATE INDEX IF NOT EXISTS IX_Attendance_Date     ON AttendanceRecords (WorkDate);
CREATE INDEX IF NOT EXISTS IX_Attendance_Employee ON AttendanceRecords (EmployeeId);
