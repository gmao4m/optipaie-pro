-- ============================================================================
--  OptiPaie PRO - Migration 0028 : Performance module — FULL REBUILD
-- ----------------------------------------------------------------------------
--  The old evaluation module (reviews / cycles / goals / career events /
--  calibration, tables from migrations 0014, 0022, 0027) is replaced by a
--  simpler, fairer design:
--    * per-department Templates with weighted Criteria (simple or weighted mode)
--    * numeric KPI criteria (target / achieved -> achievement %)
--    * a continuous 👍/👎 Behavior log that feeds periodic evaluations with facts
--    * evaluation Periods (weekly / monthly / yearly) grouping Evaluations
--    * one normalised 0-100 score per evaluation + a 5-band classification
--
--  Everything references the shared Employees / Companies tables by id only —
--  no employee, company or payroll data is copied. Payroll, attendance, CNAS
--  and contracts are untouched.
--
--  Convention (as elsewhere): decimals & dates stored as invariant-culture TEXT,
--  bools as INTEGER with a 0/1 CHECK, soft delete via IsDeleted.
-- ============================================================================

-- 1. Drop the old module (children before parents) --------------------------
DROP TABLE IF EXISTS PerformanceCriteria;
DROP TABLE IF EXISTS PerformanceTemplateCriteria;
DROP TABLE IF EXISTS PerformanceReviews;
DROP TABLE IF EXISTS PerformanceTemplates;
DROP TABLE IF EXISTS PerformanceCycles;
DROP TABLE IF EXISTS PerformanceGoals;
DROP TABLE IF EXISTS PerformanceGoalTemplates;
DROP TABLE IF EXISTS PerformanceCareerEvents;
DROP TABLE IF EXISTS PerformanceDeptSettings;

-- 2. Templates --------------------------------------------------------------
--  A reusable evaluation grid, owned by a company or shipped built-in
--  (CompanyId NULL). WeightingMode: 1 = Simple (criteria equal), 2 = Weighted.
CREATE TABLE IF NOT EXISTS EvalTemplates (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId     INTEGER NULL,
    Department    TEXT    NULL,
    Name          TEXT    NOT NULL,
    Description   TEXT,
    WeightingMode INTEGER NOT NULL DEFAULT 1 CHECK (WeightingMode IN (1, 2)),
    IsBuiltIn     INTEGER NOT NULL DEFAULT 0 CHECK (IsBuiltIn IN (0, 1)),
    IsDefault     INTEGER NOT NULL DEFAULT 0 CHECK (IsDefault IN (0, 1)),
    CreatedAtUtc  TEXT    NOT NULL,
    UpdatedAtUtc  TEXT,
    IsDeleted     INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_EvalTemplate_Company
        FOREIGN KEY (CompanyId) REFERENCES Companies (Id)
        ON UPDATE CASCADE ON DELETE CASCADE
);

--  Category: 1 Behavioral, 2 Technical, 3 Administrative, 4 KPI.
--  ScoreType: 1 Stars(/5), 2 Score(/20), 3 Percent(%). KPI ignores ScoreType
--  (its score is the achievement %). WeightPercent used in Weighted mode.
CREATE TABLE IF NOT EXISTS EvalCriteria (
    Id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    TemplateId     INTEGER NOT NULL,
    Name           TEXT    NOT NULL,
    Category       INTEGER NOT NULL DEFAULT 1 CHECK (Category IN (1, 2, 3, 4)),
    ScoreType      INTEGER NOT NULL DEFAULT 1 CHECK (ScoreType IN (1, 2, 3)),
    WeightPercent  TEXT    NOT NULL DEFAULT '0',
    KpiTarget      TEXT    NULL,
    HigherIsBetter INTEGER NOT NULL DEFAULT 1 CHECK (HigherIsBetter IN (0, 1)),
    SortOrder      INTEGER NOT NULL DEFAULT 0,
    IsDeleted      INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_EvalCriterion_Template
        FOREIGN KEY (TemplateId) REFERENCES EvalTemplates (Id)
        ON UPDATE CASCADE ON DELETE CASCADE
);

-- 3. Periods ----------------------------------------------------------------
--  Cadence: 1 Weekly, 2 Monthly, 3 Yearly. Status: 1 Open, 2 Closed.
CREATE TABLE IF NOT EXISTS EvalPeriods (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId    INTEGER NOT NULL,
    Name         TEXT    NOT NULL,
    Cadence      INTEGER NOT NULL DEFAULT 2 CHECK (Cadence IN (1, 2, 3)),
    StartDate    TEXT    NOT NULL,
    EndDate      TEXT    NOT NULL,
    Status       INTEGER NOT NULL DEFAULT 1 CHECK (Status IN (1, 2)),
    CreatedAtUtc TEXT    NOT NULL,
    UpdatedAtUtc TEXT,
    IsDeleted    INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_EvalPeriod_Company
        FOREIGN KEY (CompanyId) REFERENCES Companies (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

-- 4. Evaluations ------------------------------------------------------------
--  One employee's evaluation within a period. TotalScore is the normalised
--  0-100 result; the classification band is derived from it. Status: 1 Pending,
--  2 Done. Department & WeightingMode are snapshotted for stable reporting.
CREATE TABLE IF NOT EXISTS Evaluations (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    PeriodId      INTEGER NOT NULL,
    EmployeeId    INTEGER NOT NULL,
    TemplateId    INTEGER NULL,
    Department    TEXT,
    WeightingMode INTEGER NOT NULL DEFAULT 1 CHECK (WeightingMode IN (1, 2)),
    TotalScore    TEXT    NOT NULL DEFAULT '0',
    Status        INTEGER NOT NULL DEFAULT 1 CHECK (Status IN (1, 2)),
    EvaluatedDate TEXT,
    Evaluator     TEXT,
    Note          TEXT,
    CreatedAtUtc  TEXT    NOT NULL,
    UpdatedAtUtc  TEXT,
    IsDeleted     INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Evaluation_Period
        FOREIGN KEY (PeriodId) REFERENCES EvalPeriods (Id)
        ON UPDATE CASCADE ON DELETE CASCADE,
    CONSTRAINT FK_Evaluation_Employee
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

--  One scored criterion line of an evaluation (snapshotted from the template).
--  RawValue is what the evaluator entered (stars 1-5, /20, or %); NULL until
--  scored. NormalizedScore is the 0-100 value the total is built from.
CREATE TABLE IF NOT EXISTS EvaluationScores (
    Id              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    EvaluationId    INTEGER NOT NULL,
    CriterionName   TEXT    NOT NULL,
    Category        INTEGER NOT NULL DEFAULT 1 CHECK (Category IN (1, 2, 3, 4)),
    ScoreType       INTEGER NOT NULL DEFAULT 1 CHECK (ScoreType IN (1, 2, 3)),
    WeightPercent   TEXT    NOT NULL DEFAULT '0',
    RawValue        TEXT    NULL,
    KpiTarget       TEXT    NULL,
    KpiActual       TEXT    NULL,
    HigherIsBetter  INTEGER NOT NULL DEFAULT 1 CHECK (HigherIsBetter IN (0, 1)),
    NormalizedScore TEXT    NOT NULL DEFAULT '0',
    Note            TEXT,
    SortOrder       INTEGER NOT NULL DEFAULT 0,
    IsDeleted       INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_EvalScore_Evaluation
        FOREIGN KEY (EvaluationId) REFERENCES Evaluations (Id)
        ON UPDATE CASCADE ON DELETE CASCADE
);

-- 5. Behavior log -----------------------------------------------------------
--  Continuous 👍 / 👎 facts captured as they happen, shown next to the
--  evaluation screen so scoring is grounded in reality, not memory.
CREATE TABLE IF NOT EXISTS BehaviorLogs (
    Id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId    INTEGER NOT NULL,
    EmployeeId   INTEGER NOT NULL,
    IsPositive   INTEGER NOT NULL CHECK (IsPositive IN (0, 1)),
    Note         TEXT,
    OccurredAt   TEXT    NOT NULL,
    CreatedAtUtc TEXT    NOT NULL,
    IsDeleted    INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Behavior_Employee
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT,
    CONSTRAINT FK_Behavior_Company
        FOREIGN KEY (CompanyId) REFERENCES Companies (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

-- 6. Indexes ----------------------------------------------------------------
CREATE INDEX IF NOT EXISTS IX_EvalTemplate_Company ON EvalTemplates (CompanyId);
CREATE INDEX IF NOT EXISTS IX_EvalCriterion_Tpl    ON EvalCriteria (TemplateId);
CREATE INDEX IF NOT EXISTS IX_EvalPeriod_Company   ON EvalPeriods (CompanyId);
CREATE INDEX IF NOT EXISTS IX_Evaluation_Period    ON Evaluations (PeriodId);
CREATE INDEX IF NOT EXISTS IX_Evaluation_Employee  ON Evaluations (EmployeeId);
CREATE INDEX IF NOT EXISTS IX_EvalScore_Eval       ON EvaluationScores (EvaluationId);
CREATE INDEX IF NOT EXISTS IX_Behavior_Employee    ON BehaviorLogs (EmployeeId);
CREATE INDEX IF NOT EXISTS IX_Behavior_Company     ON BehaviorLogs (CompanyId);

-- 7. Seed the one built-in default template ---------------------------------
--  General criteria, simple mode, 1-5 stars. Companies duplicate this into
--  per-department templates. CompanyId NULL = shipped built-in (read-only).
-- IsDefault stays 0: it is the fallback grid (ResolveTemplate finds it when a company has
-- none of its own), not a company's chosen default.
INSERT INTO EvalTemplates (CompanyId, Department, Name, Description, WeightingMode, IsBuiltIn, IsDefault, CreatedAtUtc)
VALUES (NULL, NULL, 'Modèle de base', 'Critères généraux — point de départ pour vos modèles par département.', 1, 1, 0, datetime('now'));

INSERT INTO EvalCriteria (TemplateId, Name, Category, ScoreType, WeightPercent, KpiTarget, HigherIsBetter, SortOrder) VALUES
 ((SELECT Id FROM EvalTemplates WHERE IsBuiltIn = 1 AND Department IS NULL), 'Qualité du travail',       1, 1, '20', NULL, 1, 0),
 ((SELECT Id FROM EvalTemplates WHERE IsBuiltIn = 1 AND Department IS NULL), 'Respect des délais',        1, 1, '20', NULL, 1, 1),
 ((SELECT Id FROM EvalTemplates WHERE IsBuiltIn = 1 AND Department IS NULL), 'Travail d''équipe',         1, 1, '20', NULL, 1, 2),
 ((SELECT Id FROM EvalTemplates WHERE IsBuiltIn = 1 AND Department IS NULL), 'Initiative',                1, 1, '20', NULL, 1, 3),
 ((SELECT Id FROM EvalTemplates WHERE IsBuiltIn = 1 AND Department IS NULL), 'Assiduité et discipline',   1, 1, '20', NULL, 1, 4);
