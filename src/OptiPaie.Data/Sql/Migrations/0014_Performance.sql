-- ============================================================================
--  OptiPaie PRO - Migration 0014 : Performance module (Évaluations)
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. A review references the shared Employees table by foreign
--  key; neither employee nor company data is copied. The attendance context shown
--  in a review is pulled live from the Attendance module, never stored here.
--
--  Scores/weights are stored as invariant TEXT (existing decimal convention);
--  dates as ISO-8601.
-- ============================================================================

CREATE TABLE IF NOT EXISTS PerformanceReviews (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    EmployeeId   INTEGER NOT NULL,
    PeriodYear   INTEGER NOT NULL,
    PeriodLabel  TEXT,
    Status       INTEGER NOT NULL DEFAULT 1 CHECK (Status IN (1, 2)),
    ReviewDate   TEXT    NOT NULL,
    Reviewer     TEXT,
    OverallScore TEXT    NOT NULL DEFAULT '0',
    Comments     TEXT,
    CreatedAtUtc TEXT    NOT NULL,
    UpdatedAtUtc TEXT,
    IsDeleted    INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Review_Employees
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS PerformanceCriteria (
    Id        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ReviewId  INTEGER NOT NULL,
    Label     TEXT    NOT NULL,
    Weight    TEXT    NOT NULL DEFAULT '1',
    Score     TEXT    NOT NULL DEFAULT '0',
    Comment   TEXT,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    IsDeleted INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Criterion_Reviews
        FOREIGN KEY (ReviewId) REFERENCES PerformanceReviews (Id)
        ON UPDATE CASCADE ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_Review_Employee  ON PerformanceReviews (EmployeeId);
CREATE INDEX IF NOT EXISTS IX_Review_Period    ON PerformanceReviews (PeriodYear);
CREATE INDEX IF NOT EXISTS IX_Criterion_Review ON PerformanceCriteria (ReviewId);
