-- ============================================================================
--  OptiPaie PRO - Migration 0011 : one canonical text form for attendance days
-- ----------------------------------------------------------------------------
--  System.Data.SQLite renders the same day differently depending on the
--  DateTime.Kind it is handed: "2025-06-01 00:00:00" (Utc) versus
--  "2025-06-01 00:00:00Z" (Unspecified). Rows written before SqliteDate.Day was
--  introduced carry the trailing Z, so an equality lookup from another module
--  found nothing. Normalise them to the canonical form.
--
--  Data-only: no schema change, and days keep their exact calendar value.
-- ============================================================================

UPDATE AttendanceRecords
   SET WorkDate = rtrim(WorkDate, 'Z')
 WHERE WorkDate LIKE '%Z';
