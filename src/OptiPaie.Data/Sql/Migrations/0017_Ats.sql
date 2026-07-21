-- ============================================================================
--  OptiPaie PRO - Migration 0017 : Recruitment / ATS module (Recrutement)
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. A posting belongs to a Company. Candidates live only in this
--  module (they are not employees); when a candidate is hired the service creates
--  a SHARED employee and stores its id on the candidate. Neither company nor
--  employee data is otherwise copied.
--
--  Dates are ISO-8601.
-- ============================================================================

CREATE TABLE IF NOT EXISTS JobPostings (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId    INTEGER NOT NULL,
    Title        TEXT    NOT NULL,
    Department   TEXT,
    Description  TEXT,
    Status       INTEGER NOT NULL DEFAULT 1 CHECK (Status IN (1, 2, 3)),
    OpenDate     TEXT    NOT NULL,
    Positions    INTEGER NOT NULL DEFAULT 1 CHECK (Positions >= 1),
    Notes        TEXT,
    CreatedAtUtc TEXT    NOT NULL,
    UpdatedAtUtc TEXT,
    IsDeleted    INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Posting_Companies
        FOREIGN KEY (CompanyId) REFERENCES Companies (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS Candidates (
    Id              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    PostingId       INTEGER NOT NULL,
    FirstName       TEXT,
    LastName        TEXT    NOT NULL,
    Phone           TEXT,
    Email           TEXT,
    Stage           INTEGER NOT NULL DEFAULT 1 CHECK (Stage IN (1, 2, 3, 4, 5, 6)),
    Rating          INTEGER NOT NULL DEFAULT 0 CHECK (Rating BETWEEN 0 AND 5),
    Source          TEXT,
    Notes           TEXT,
    AppliedDate     TEXT    NOT NULL,
    HiredEmployeeId INTEGER,
    CreatedAtUtc    TEXT    NOT NULL,
    UpdatedAtUtc    TEXT,
    IsDeleted       INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Candidate_Postings
        FOREIGN KEY (PostingId) REFERENCES JobPostings (Id)
        ON UPDATE CASCADE ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_Posting_Company    ON JobPostings (CompanyId);
CREATE INDEX IF NOT EXISTS IX_Candidate_Posting  ON Candidates (PostingId);
CREATE INDEX IF NOT EXISTS IX_Candidate_Stage    ON Candidates (Stage);
