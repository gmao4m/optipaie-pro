-- ============================================================================
--  OptiPaie PRO - Migration 0033 : Recruitment module extension
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. New nullable columns on the existing recruitment tables and
--  two small new tables (interviews, candidate attachments). No CHECK is altered,
--  no table is rebuilt, no existing row is touched. "Désisté" is NOT a new Stage
--  value (the CHECK stays 1..6): Stage=Rejected(6) is the negative-closure umbrella
--  and ClosureType/ClosureReason qualify it. "Annulé" reuses Status=Closed(2).
-- ============================================================================

-- Job postings: contract type, deadline, recruiter, and closure qualification.
ALTER TABLE JobPostings ADD COLUMN ContractType    INTEGER;
ALTER TABLE JobPostings ADD COLUMN Deadline        TEXT;
ALTER TABLE JobPostings ADD COLUMN ResponsibleName TEXT;
ALTER TABLE JobPostings ADD COLUMN ClosureType     INTEGER;
ALTER TABLE JobPostings ADD COLUMN ClosureReason   TEXT;

-- Candidates: education / experience + the closure qualification (Refusé vs Désisté + motif).
ALTER TABLE Candidates ADD COLUMN EducationLevel   TEXT;
ALTER TABLE Candidates ADD COLUMN ExperienceYears  INTEGER;
ALTER TABLE Candidates ADD COLUMN ClosureType      INTEGER;   -- 1 = Refusé, 2 = Désisté (see CandidateClosure)
ALTER TABLE Candidates ADD COLUMN ClosureReason    TEXT;
ALTER TABLE Candidates ADD COLUMN ClosureDate      TEXT;

-- Interviews: several per candidate. Deliberately minimal.
CREATE TABLE IF NOT EXISTS Interviews (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CandidateId   INTEGER NOT NULL,
    ScheduledDate TEXT    NOT NULL,
    Type          TEXT,
    Interviewer   TEXT,
    Result        TEXT,
    Notes         TEXT,
    CreatedAtUtc  TEXT    NOT NULL,
    IsDeleted     INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Interview_Candidate
        FOREIGN KEY (CandidateId) REFERENCES Candidates (Id)
        ON UPDATE CASCADE ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_Interview_Candidate ON Interviews (CandidateId);

-- Candidate attachments: CV + documents, stored on disk; the row keeps the pointer.
CREATE TABLE IF NOT EXISTS CandidateAttachments (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CandidateId  INTEGER NOT NULL,
    FileName     TEXT    NOT NULL,
    RelativePath TEXT    NOT NULL,
    Kind         TEXT,
    AddedAt      TEXT    NOT NULL,
    CreatedAtUtc TEXT    NOT NULL,
    IsDeleted    INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Attach_Candidate
        FOREIGN KEY (CandidateId) REFERENCES Candidates (Id)
        ON UPDATE CASCADE ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS IX_Attach_Candidate ON CandidateAttachments (CandidateId);
