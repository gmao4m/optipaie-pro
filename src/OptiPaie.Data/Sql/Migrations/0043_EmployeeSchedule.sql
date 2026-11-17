-- ============================================================================
--  OptiPaie PRO - Migration 0043 : per-employee weekly work pattern
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. Some employees (a security guard, a weekend shift) work days
--  that are rest days for the rest of the company. The work calendar is per
--  company, so until now those people were wrongly treated as off every week —
--  in the daily list, in their workable days, and in their pay.
--
--  Each employee gets an OPTIONAL own weekly pattern that overrides the company
--  calendar. The default is "follows the company": UsesOwnSchedule = 0, so NOTHING
--  changes for anyone unless someone explicitly turns it on. The seven day columns
--  carry the Algerian default (Sun-Thu work, Fri-Sat rest) only as a sensible
--  starting point for when the override is switched on; while UsesOwnSchedule = 0
--  they are ignored and the company calendar decides.
--
--  Existing rows gain the columns with these defaults, keep UsesOwnSchedule = 0,
--  and therefore compute, prorate and print exactly as before.
-- ============================================================================

ALTER TABLE Employees ADD COLUMN UsesOwnSchedule INTEGER NOT NULL DEFAULT 0;
ALTER TABLE Employees ADD COLUMN SchedMon INTEGER NOT NULL DEFAULT 1;
ALTER TABLE Employees ADD COLUMN SchedTue INTEGER NOT NULL DEFAULT 1;
ALTER TABLE Employees ADD COLUMN SchedWed INTEGER NOT NULL DEFAULT 1;
ALTER TABLE Employees ADD COLUMN SchedThu INTEGER NOT NULL DEFAULT 1;
ALTER TABLE Employees ADD COLUMN SchedFri INTEGER NOT NULL DEFAULT 0;
ALTER TABLE Employees ADD COLUMN SchedSat INTEGER NOT NULL DEFAULT 0;
ALTER TABLE Employees ADD COLUMN SchedSun INTEGER NOT NULL DEFAULT 1;
