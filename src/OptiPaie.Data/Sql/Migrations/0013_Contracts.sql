-- ============================================================================
--  OptiPaie PRO - Migration 0013 : Contracts module (Gestion des contrats)
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. A contract references the shared Employees table by foreign
--  key; neither employee nor company data is copied. Activating a contract writes
--  its terms back onto that shared employee (handled by the service), so payroll
--  needs no import step.
--
--  Amounts are stored as invariant TEXT (existing money convention); dates as
--  ISO-8601. ContractType reuses the payroll enum (1 CDI, 2 CDD, 3 Apprenticeship,
--  4 Internship, 99 Other).
-- ============================================================================

CREATE TABLE IF NOT EXISTS EmploymentContracts (
    Id                 INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    EmployeeId         INTEGER NOT NULL,
    Type               INTEGER NOT NULL CHECK (Type IN (1, 2, 3, 4, 99)),
    Status             INTEGER NOT NULL DEFAULT 1 CHECK (Status IN (1, 2, 3, 4, 5)),
    Reference          TEXT,
    Position           TEXT,
    BaseSalary         TEXT    NOT NULL DEFAULT '0',
    StartDate          TEXT    NOT NULL,
    EndDate            TEXT,
    TrialPeriodDays    INTEGER NOT NULL DEFAULT 0 CHECK (TrialPeriodDays >= 0),
    PreviousContractId INTEGER,
    SignedDate         TEXT,
    Notes              TEXT,
    CreatedAtUtc       TEXT    NOT NULL,
    UpdatedAtUtc       TEXT,
    IsDeleted          INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT CK_Contract_Range CHECK (EndDate IS NULL OR EndDate >= StartDate),
    CONSTRAINT FK_Contract_Employees
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS IX_Contract_Employee ON EmploymentContracts (EmployeeId);
CREATE INDEX IF NOT EXISTS IX_Contract_Status   ON EmploymentContracts (Status);
CREATE INDEX IF NOT EXISTS IX_Contract_EndDate  ON EmploymentContracts (EndDate);
