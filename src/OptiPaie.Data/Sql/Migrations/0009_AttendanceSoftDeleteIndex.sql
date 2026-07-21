-- ============================================================================
--  OptiPaie PRO - Migration 0009 : Attendance unique index vs soft delete
-- ----------------------------------------------------------------------------
--  0008 made (EmployeeId, WorkDate) unique across ALL rows, including
--  soft-deleted ones. Deleting a day and then recording it again therefore hit
--  the constraint. The rule we actually want is "one LIVE record per employee
--  and day", so the index becomes partial; deleted rows stay as history.
--
--  Purely additive: no data is touched, only the index definition changes.
-- ============================================================================

DROP INDEX IF EXISTS UX_Attendance_Employee_Date;

CREATE UNIQUE INDEX IF NOT EXISTS UX_Attendance_Employee_Date
    ON AttendanceRecords (EmployeeId, WorkDate)
    WHERE IsDeleted = 0;
