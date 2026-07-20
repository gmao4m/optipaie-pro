-- ============================================================================
--  OptiPaie DZ - Migration 0001 : Initial relational schema
-- ----------------------------------------------------------------------------
--  Conventions
--    * Column names match entity property names exactly (Dapper default mapping).
--    * Money & rates  -> TEXT  (stored/read as invariant-culture decimal strings).
--    * Booleans/enums -> INTEGER with CHECK guards.
--    * Timestamps     -> TEXT  (ISO-8601 UTC).
--    * Foreign keys   : RESTRICT on anything payroll history references; CASCADE
--                       only inside a payroll aggregate (Run -> Payslip -> Detail).
-- ============================================================================

-- --------------------------------------------------------------------------
--  Companies
-- --------------------------------------------------------------------------
CREATE TABLE Companies (
    Id                 INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    NameFr             TEXT    NOT NULL,
    NameAr             TEXT,
    LegalForm          TEXT,
    AddressFr          TEXT,
    AddressAr          TEXT,
    Nif                TEXT,
    Nis                TEXT,
    Rc                 TEXT,
    ArticleImposition  TEXT,
    CnasEmployerNumber TEXT,
    Phone              TEXT,
    Email              TEXT,
    Logo               BLOB,
    CreatedAtUtc       TEXT    NOT NULL,
    UpdatedAtUtc       TEXT,
    IsDeleted          INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1))
);

CREATE INDEX IX_Companies_NameFr    ON Companies (NameFr);
CREATE INDEX IX_Companies_IsDeleted ON Companies (IsDeleted);

-- --------------------------------------------------------------------------
--  Employees  (each belongs to exactly one company)
-- --------------------------------------------------------------------------
CREATE TABLE Employees (
    Id               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId        INTEGER NOT NULL,
    LastNameFr       TEXT    NOT NULL,
    LastNameAr       TEXT,
    FirstNameFr      TEXT    NOT NULL,
    FirstNameAr      TEXT,
    Gender           INTEGER NOT NULL DEFAULT 1 CHECK (Gender IN (1, 2)),
    Nss              TEXT,
    NationalId       TEXT,
    BirthDate        TEXT,
    HireDate         TEXT    NOT NULL,
    ExitDate         TEXT,
    Category         TEXT,
    Poste            TEXT,
    ContractType     INTEGER NOT NULL DEFAULT 1 CHECK (ContractType IN (1, 2, 3, 4, 99)),
    MaritalStatus    INTEGER NOT NULL DEFAULT 1 CHECK (MaritalStatus IN (1, 2, 3, 4)),
    Dependents       INTEGER NOT NULL DEFAULT 0 CHECK (Dependents >= 0),
    BaseSalary       TEXT    NOT NULL DEFAULT '0',
    PaymentMode      INTEGER NOT NULL DEFAULT 1 CHECK (PaymentMode IN (1, 2, 3)),
    Rib              TEXT,
    IsActive         INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1)),
    CreatedAtUtc     TEXT    NOT NULL,
    UpdatedAtUtc     TEXT,
    IsDeleted        INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Employees_Companies
        FOREIGN KEY (CompanyId) REFERENCES Companies (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE INDEX IX_Employees_CompanyId  ON Employees (CompanyId);
CREATE INDEX IX_Employees_LastNameFr ON Employees (LastNameFr);
CREATE INDEX IX_Employees_IsActive   ON Employees (IsActive);
CREATE INDEX IX_Employees_IsDeleted  ON Employees (IsDeleted);

-- --------------------------------------------------------------------------
--  PayrollElements  (user-creatable catalog)
-- --------------------------------------------------------------------------
CREATE TABLE PayrollElements (
    Id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    NameFr            TEXT    NOT NULL,
    NameAr            TEXT,
    Description       TEXT,
    ElementType       INTEGER NOT NULL CHECK (ElementType IN (1, 2)),
    CalculationMethod INTEGER NOT NULL CHECK (CalculationMethod IN (1, 2, 3, 4)),
    CalculationBase   INTEGER          CHECK (CalculationBase IS NULL OR CalculationBase IN (1, 2, 3)),
    DefaultAmount     TEXT,
    DefaultRate       TEXT,
    DefaultQuantity   TEXT,
    DefaultUnitPrice  TEXT,
    Periodicity       INTEGER NOT NULL DEFAULT 1 CHECK (Periodicity IN (1, 2, 3, 4)),
    IsCnasApplicable  INTEGER NOT NULL DEFAULT 0 CHECK (IsCnasApplicable IN (0, 1)),
    IsIrgApplicable   INTEGER NOT NULL DEFAULT 0 CHECK (IsIrgApplicable IN (0, 1)),
    IsIncludedInGross INTEGER NOT NULL DEFAULT 1 CHECK (IsIncludedInGross IN (0, 1)),
    ExemptionCeiling  TEXT,
    IsEditable        INTEGER NOT NULL DEFAULT 1 CHECK (IsEditable IN (0, 1)),
    IsEnabled         INTEGER NOT NULL DEFAULT 1 CHECK (IsEnabled IN (0, 1)),
    IsSystem          INTEGER NOT NULL DEFAULT 0 CHECK (IsSystem IN (0, 1)),
    DisplayOrder      INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc      TEXT    NOT NULL,
    UpdatedAtUtc      TEXT,
    IsDeleted         INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1))
);

CREATE INDEX IX_PayrollElements_DisplayOrder ON PayrollElements (DisplayOrder);
CREATE INDEX IX_PayrollElements_IsEnabled    ON PayrollElements (IsEnabled);
CREATE INDEX IX_PayrollElements_IsDeleted    ON PayrollElements (IsDeleted);

-- --------------------------------------------------------------------------
--  EmployeeElements  (assignment + per-employee overrides)
-- --------------------------------------------------------------------------
CREATE TABLE EmployeeElements (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    EmployeeId   INTEGER NOT NULL,
    ElementId    INTEGER NOT NULL,
    Amount       TEXT,
    Rate         TEXT,
    Quantity     TEXT,
    UnitPrice    TEXT,
    IsActive     INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1)),
    CreatedAtUtc TEXT    NOT NULL,
    CONSTRAINT FK_EmployeeElements_Employees
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE CASCADE,
    CONSTRAINT FK_EmployeeElements_Elements
        FOREIGN KEY (ElementId) REFERENCES PayrollElements (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE UNIQUE INDEX UX_EmployeeElements_Employee_Element
    ON EmployeeElements (EmployeeId, ElementId);

-- --------------------------------------------------------------------------
--  PayrollRuns  (one company + one period batch)
-- --------------------------------------------------------------------------
CREATE TABLE PayrollRuns (
    Id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId      INTEGER NOT NULL,
    PeriodYear     INTEGER NOT NULL CHECK (PeriodYear BETWEEN 2000 AND 2100),
    PeriodMonth    INTEGER NOT NULL CHECK (PeriodMonth BETWEEN 1 AND 12),
    RunStatus      INTEGER NOT NULL DEFAULT 1 CHECK (RunStatus IN (1, 2, 3, 4)),
    GeneratedAtUtc TEXT,
    EngineVersion  TEXT,
    CreatedAtUtc   TEXT    NOT NULL,
    CONSTRAINT FK_PayrollRuns_Companies
        FOREIGN KEY (CompanyId) REFERENCES Companies (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

-- One run per company per period; also the primary archive-search path.
CREATE UNIQUE INDEX UX_PayrollRuns_Company_Period
    ON PayrollRuns (CompanyId, PeriodYear, PeriodMonth);
CREATE INDEX IX_PayrollRuns_Period ON PayrollRuns (PeriodYear, PeriodMonth);
CREATE INDEX IX_PayrollRuns_Status ON PayrollRuns (RunStatus);

-- --------------------------------------------------------------------------
--  Payslips  (one employee bulletin within a run)
-- --------------------------------------------------------------------------
CREATE TABLE Payslips (
    Id                   INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    RunId                INTEGER NOT NULL,
    EmployeeId           INTEGER NOT NULL,
    SalaireBrut          TEXT    NOT NULL DEFAULT '0',
    BaseCotisable        TEXT    NOT NULL DEFAULT '0',
    CnasEmployee         TEXT    NOT NULL DEFAULT '0',
    CnasEmployer         TEXT    NOT NULL DEFAULT '0',
    BaseImposable        TEXT    NOT NULL DEFAULT '0',
    IrgBrut              TEXT    NOT NULL DEFAULT '0',
    Abattement           TEXT    NOT NULL DEFAULT '0',
    Irg                  TEXT    NOT NULL DEFAULT '0',
    NetSalaire           TEXT    NOT NULL DEFAULT '0',
    CnasEmployeeRateUsed TEXT    NOT NULL DEFAULT '0',
    CnasEmployerRateUsed TEXT    NOT NULL DEFAULT '0',
    WorkedDays           TEXT    NOT NULL DEFAULT '0',
    WorkedHours          TEXT    NOT NULL DEFAULT '0',
    EngineVersion        TEXT,
    GeneratedAtUtc       TEXT    NOT NULL,
    CreatedAtUtc         TEXT    NOT NULL,
    CONSTRAINT FK_Payslips_Runs
        FOREIGN KEY (RunId) REFERENCES PayrollRuns (Id)
        ON UPDATE CASCADE ON DELETE CASCADE,
    CONSTRAINT FK_Payslips_Employees
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE UNIQUE INDEX UX_Payslips_Run_Employee ON Payslips (RunId, EmployeeId);
CREATE INDEX IX_Payslips_EmployeeId          ON Payslips (EmployeeId);

-- --------------------------------------------------------------------------
--  PayrollDetails  (frozen lines of a payslip)
-- --------------------------------------------------------------------------
CREATE TABLE PayrollDetails (
    Id               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    PayslipId        INTEGER NOT NULL,
    ElementId        INTEGER,
    LabelFr          TEXT    NOT NULL,
    LabelAr          TEXT,
    ElementType      INTEGER NOT NULL CHECK (ElementType IN (1, 2)),
    Base             TEXT,
    Rate             TEXT,
    Quantity         TEXT,
    UnitPrice        TEXT,
    Amount           TEXT    NOT NULL DEFAULT '0',
    IsCnasApplicable INTEGER NOT NULL DEFAULT 0 CHECK (IsCnasApplicable IN (0, 1)),
    IsIrgApplicable  INTEGER NOT NULL DEFAULT 0 CHECK (IsIrgApplicable IN (0, 1)),
    DisplayOrder     INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc     TEXT    NOT NULL,
    CONSTRAINT FK_PayrollDetails_Payslips
        FOREIGN KEY (PayslipId) REFERENCES Payslips (Id)
        ON UPDATE CASCADE ON DELETE CASCADE,
    CONSTRAINT FK_PayrollDetails_Elements
        FOREIGN KEY (ElementId) REFERENCES PayrollElements (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE INDEX IX_PayrollDetails_PayslipId ON PayrollDetails (PayslipId);

-- --------------------------------------------------------------------------
--  ArchiveDocuments  (immutable frozen rendering; RESTRICT protects archives)
-- --------------------------------------------------------------------------
CREATE TABLE ArchiveDocuments (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    PayslipId    INTEGER NOT NULL,
    LanguageCode TEXT    NOT NULL,
    PdfContent   BLOB,
    SnapshotJson TEXT,
    Checksum     TEXT,
    CreatedAtUtc TEXT    NOT NULL,
    CONSTRAINT FK_ArchiveDocuments_Payslips
        FOREIGN KEY (PayslipId) REFERENCES Payslips (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE INDEX IX_ArchiveDocuments_PayslipId ON ArchiveDocuments (PayslipId);

-- --------------------------------------------------------------------------
--  LegalParameters  (configurable payroll values, effective-dated)
-- --------------------------------------------------------------------------
CREATE TABLE LegalParameters (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    ParamKey      TEXT    NOT NULL,
    ParamValue    TEXT    NOT NULL,
    EffectiveFrom TEXT    NOT NULL,
    IsActive      INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1)),
    Description   TEXT,
    CreatedAtUtc  TEXT    NOT NULL
);

CREATE UNIQUE INDEX UX_LegalParameters_Key_Effective
    ON LegalParameters (ParamKey, EffectiveFrom);
CREATE INDEX IX_LegalParameters_Key ON LegalParameters (ParamKey);

-- --------------------------------------------------------------------------
--  AppSettings  (UI/application preferences, key/value)
-- --------------------------------------------------------------------------
CREATE TABLE AppSettings (
    SettingKey   TEXT NOT NULL PRIMARY KEY,
    SettingValue TEXT,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT
);

-- --------------------------------------------------------------------------
--  Languages  (supported UI languages metadata)
-- --------------------------------------------------------------------------
CREATE TABLE Languages (
    Code         TEXT    NOT NULL PRIMARY KEY,
    NameNative   TEXT    NOT NULL,
    Direction    INTEGER NOT NULL CHECK (Direction IN (1, 2)),
    FontFamily   TEXT,
    IsEnabled    INTEGER NOT NULL DEFAULT 1 CHECK (IsEnabled IN (0, 1)),
    DisplayOrder INTEGER NOT NULL DEFAULT 0
);

-- --------------------------------------------------------------------------
--  BackupRecords  (backup audit log)
-- --------------------------------------------------------------------------
CREATE TABLE BackupRecords (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    FilePath      TEXT    NOT NULL,
    BackupType    INTEGER NOT NULL CHECK (BackupType IN (1, 2)),
    SizeBytes     INTEGER NOT NULL DEFAULT 0,
    Checksum      TEXT,
    SchemaVersion INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc  TEXT    NOT NULL
);

CREATE INDEX IX_BackupRecords_CreatedAt ON BackupRecords (CreatedAtUtc);
