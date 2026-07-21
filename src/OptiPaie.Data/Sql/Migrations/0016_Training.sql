-- ============================================================================
--  OptiPaie PRO - Migration 0016 : Training module (Formation)
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. A session belongs to a Company; participants reference the
--  SHARED Employees table. Neither employee nor company data is copied.
--
--  Cost is stored as invariant TEXT (existing money convention); dates as ISO-8601.
-- ============================================================================

CREATE TABLE IF NOT EXISTS TrainingSessions (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId    INTEGER NOT NULL,
    Title        TEXT    NOT NULL,
    Category     TEXT,
    Provider     TEXT,
    Status       INTEGER NOT NULL DEFAULT 1 CHECK (Status IN (1, 2, 3, 4)),
    StartDate    TEXT    NOT NULL,
    EndDate      TEXT,
    Location     TEXT,
    Cost         TEXT    NOT NULL DEFAULT '0',
    Notes        TEXT,
    CreatedAtUtc TEXT    NOT NULL,
    UpdatedAtUtc TEXT,
    IsDeleted    INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT CK_Training_Range CHECK (EndDate IS NULL OR EndDate >= StartDate),
    CONSTRAINT FK_Training_Companies
        FOREIGN KEY (CompanyId) REFERENCES Companies (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS TrainingParticipants (
    Id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    SessionId      INTEGER NOT NULL,
    EmployeeId     INTEGER NOT NULL,
    Result         INTEGER NOT NULL DEFAULT 1 CHECK (Result IN (1, 2, 3, 4)),
    Score          TEXT,
    CertificateRef TEXT,
    Notes          TEXT,
    CreatedAtUtc   TEXT    NOT NULL,
    IsDeleted      INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Participant_Sessions
        FOREIGN KEY (SessionId) REFERENCES TrainingSessions (Id)
        ON UPDATE CASCADE ON DELETE CASCADE,
    CONSTRAINT FK_Participant_Employees
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

-- One live enrolment per employee and session.
CREATE UNIQUE INDEX IF NOT EXISTS UX_Participant_Session_Employee
    ON TrainingParticipants (SessionId, EmployeeId)
    WHERE IsDeleted = 0;

CREATE INDEX IF NOT EXISTS IX_Training_Company     ON TrainingSessions (CompanyId);
CREATE INDEX IF NOT EXISTS IX_Participant_Session  ON TrainingParticipants (SessionId);
CREATE INDEX IF NOT EXISTS IX_Participant_Employee ON TrainingParticipants (EmployeeId);
