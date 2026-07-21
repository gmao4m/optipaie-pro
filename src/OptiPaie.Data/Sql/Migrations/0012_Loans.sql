-- ============================================================================
--  OptiPaie PRO - Migration 0012 : Loans module (Prêts et avances)
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. A loan references the shared Employees table by foreign key;
--  neither employee nor company data is copied. The outstanding balance is never
--  stored: it is derived from the recorded repayments, one per (loan, period).
--
--  Amounts are stored as invariant TEXT (existing money convention); dates as
--  ISO-8601.
-- ============================================================================

CREATE TABLE IF NOT EXISTS Loans (
    Id                 INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    EmployeeId         INTEGER NOT NULL,
    Type               INTEGER NOT NULL CHECK (Type IN (1, 2)),
    Status             INTEGER NOT NULL DEFAULT 1 CHECK (Status IN (1, 2, 3, 4)),
    Principal          TEXT    NOT NULL DEFAULT '0',
    MonthlyInstallment TEXT    NOT NULL DEFAULT '0',
    StartYear          INTEGER NOT NULL,
    StartMonth         INTEGER NOT NULL CHECK (StartMonth BETWEEN 1 AND 12),
    Reason             TEXT,
    Notes              TEXT,
    CreatedAtUtc       TEXT    NOT NULL,
    UpdatedAtUtc       TEXT,
    IsDeleted          INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Loan_Employees
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS LoanRepayments (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    LoanId       INTEGER NOT NULL,
    Year         INTEGER NOT NULL,
    Month        INTEGER NOT NULL CHECK (Month BETWEEN 1 AND 12),
    Amount       TEXT    NOT NULL DEFAULT '0',
    IsManual     INTEGER NOT NULL DEFAULT 0 CHECK (IsManual IN (0, 1)),
    CreatedAtUtc TEXT    NOT NULL,
    IsDeleted    INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Repayment_Loans
        FOREIGN KEY (LoanId) REFERENCES Loans (Id)
        ON UPDATE CASCADE ON DELETE CASCADE
);

-- One live recovery per loan and period (the anti-double-deduction rule).
CREATE UNIQUE INDEX IF NOT EXISTS UX_Repayment_Loan_Period
    ON LoanRepayments (LoanId, Year, Month)
    WHERE IsDeleted = 0;

CREATE INDEX IF NOT EXISTS IX_Loan_Employee     ON Loans (EmployeeId);
CREATE INDEX IF NOT EXISTS IX_Loan_Status       ON Loans (Status);
CREATE INDEX IF NOT EXISTS IX_Repayment_Loan    ON LoanRepayments (LoanId);
