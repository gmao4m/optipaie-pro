-- ============================================================================
--  OptiPaie PRO - Migration 0022 : Performance & Career expansion
-- ----------------------------------------------------------------------------
--  Purely ADDITIVE. Turns the simple review CRUD (migration 0014) into the full
--  template-driven Performance & Career module: versioned evaluation templates,
--  review cycles, goals, promotions/rewards and per-department defaults.
--
--  Every table shares the existing Employees / Companies tables by FOREIGN KEY and
--  copies NO employee, company or payroll data. Nothing here reads or writes any
--  payroll table. Decimals are stored as invariant-culture TEXT (existing
--  convention); dates as ISO-8601 text.
-- ============================================================================

-- 1. Extend PerformanceReviews with cycle / template / reviewer linkage. Every new
--    column is nullable or defaulted, so the reviews created by migration 0014 stay
--    valid and keep behaving exactly as before (ScaleMax defaults to the /20 scale).
ALTER TABLE PerformanceReviews ADD COLUMN CycleId            INTEGER NULL;
ALTER TABLE PerformanceReviews ADD COLUMN TemplateId         INTEGER NULL;
ALTER TABLE PerformanceReviews ADD COLUMN ReviewerEmployeeId INTEGER NULL;
ALTER TABLE PerformanceReviews ADD COLUMN DueDate            TEXT    NULL;
ALTER TABLE PerformanceReviews ADD COLUMN ScaleMax           TEXT    NOT NULL DEFAULT '20';
ALTER TABLE PerformanceReviews ADD COLUMN SelfScore          TEXT    NULL;
ALTER TABLE PerformanceReviews ADD COLUMN SelfComments       TEXT    NULL;
ALTER TABLE PerformanceReviews ADD COLUMN Kind               INTEGER NULL;

-- 2. Evaluation templates. A template is versioned: editing a template that has
--    already been used creates a NEW version (same GroupKey, Version+1, IsCurrent=1)
--    so past reviews keep the exact criteria they were scored against. CompanyId NULL
--    marks the shipped built-in library, which is never edited in place.
CREATE TABLE IF NOT EXISTS PerformanceTemplates (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId     INTEGER NULL,                       -- NULL = global built-in library
    GroupKey      TEXT    NOT NULL,                   -- links versions of one template
    Version       INTEGER NOT NULL DEFAULT 1,
    IsCurrent     INTEGER NOT NULL DEFAULT 1 CHECK (IsCurrent IN (0, 1)),
    Kind          INTEGER NOT NULL DEFAULT 8,         -- TemplateKind (8 = Custom)
    Name          TEXT    NOT NULL,
    Description   TEXT,
    DepartmentTag TEXT,                               -- department this template targets
    ScaleMax      TEXT    NOT NULL DEFAULT '20',
    IsBuiltIn     INTEGER NOT NULL DEFAULT 0 CHECK (IsBuiltIn IN (0, 1)),
    IsArchived    INTEGER NOT NULL DEFAULT 0 CHECK (IsArchived IN (0, 1)),
    CreatedAtUtc  TEXT    NOT NULL,
    UpdatedAtUtc  TEXT,
    IsDeleted     INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1))
);

CREATE TABLE IF NOT EXISTS PerformanceTemplateCriteria (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    TemplateId    INTEGER NOT NULL,
    Label         TEXT    NOT NULL,
    WeightPercent TEXT    NOT NULL DEFAULT '0',       -- percentage weight; sums to 100
    SortOrder     INTEGER NOT NULL DEFAULT 0,
    IsDeleted     INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_TemplateCriterion_Template
        FOREIGN KEY (TemplateId) REFERENCES PerformanceTemplates (Id)
        ON UPDATE CASCADE ON DELETE CASCADE
);

-- 3. Review cycles. A cycle groups the reviews launched together for a company; its
--    completion percentage is derived live from the reviews that reference it.
CREATE TABLE IF NOT EXISTS PerformanceCycles (
    Id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId      INTEGER NOT NULL,
    Name           TEXT    NOT NULL,
    CycleType      INTEGER NOT NULL DEFAULT 1,        -- PerformanceCycleType
    StartDate      TEXT    NOT NULL,
    EndDate        TEXT    NOT NULL,
    Deadline       TEXT,
    Status         INTEGER NOT NULL DEFAULT 1,        -- PerformanceCycleStatus
    SelfAssessment INTEGER NOT NULL DEFAULT 0 CHECK (SelfAssessment IN (0, 1)),
    CreatedAtUtc   TEXT    NOT NULL,
    UpdatedAtUtc   TEXT,
    IsDeleted      INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Cycle_Company
        FOREIGN KEY (CompanyId) REFERENCES Companies (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

-- 4. Employee goals / KPIs. Roll into the next cycle as a discussion point.
CREATE TABLE IF NOT EXISTS PerformanceGoals (
    Id              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    EmployeeId      INTEGER NOT NULL,
    Title           TEXT    NOT NULL,
    Description     TEXT,
    TargetMetric    TEXT,
    DueDate         TEXT,
    ProgressPercent TEXT    NOT NULL DEFAULT '0',     -- 0..100
    Status          INTEGER NOT NULL DEFAULT 1,       -- PerformanceGoalStatus
    SourceCycleId   INTEGER NULL,
    CreatedAtUtc    TEXT    NOT NULL,
    UpdatedAtUtc    TEXT,
    IsDeleted       INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Goal_Employee
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

-- Department-level goal templates so goals aren't built from scratch each time.
CREATE TABLE IF NOT EXISTS PerformanceGoalTemplates (
    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId     INTEGER NULL,
    DepartmentTag TEXT,
    Title         TEXT    NOT NULL,
    TargetMetric  TEXT,
    Description   TEXT,
    CreatedAtUtc  TEXT    NOT NULL,
    IsDeleted     INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1))
);

-- 5. Career timeline events: promotions (old -> new position) and rewards/bonuses.
CREATE TABLE IF NOT EXISTS PerformanceCareerEvents (
    Id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    EmployeeId     INTEGER NOT NULL,
    EventType      INTEGER NOT NULL,                  -- CareerEventType
    EventDate      TEXT    NOT NULL,
    OldPosition    TEXT,
    NewPosition    TEXT,
    Amount         TEXT,                              -- reward amount (invariant decimal)
    RewardCategory TEXT,
    Reason         TEXT,
    LinkedReviewId INTEGER NULL,                      -- review that justified it
    CreatedAtUtc   TEXT    NOT NULL,
    IsDeleted      INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_Career_Employee
        FOREIGN KEY (EmployeeId) REFERENCES Employees (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

-- 6. Per-department defaults: which template and which reviewer a cycle pre-selects
--    for a department, so launching a cycle needs zero manual matching each time.
CREATE TABLE IF NOT EXISTS PerformanceDeptSettings (
    Id                        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId                 INTEGER NOT NULL,
    Department                TEXT    NOT NULL,
    DefaultTemplateGroupKey   TEXT,
    DefaultReviewerEmployeeId INTEGER NULL,
    CreatedAtUtc              TEXT    NOT NULL,
    UpdatedAtUtc              TEXT,
    IsDeleted                 INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1)),
    CONSTRAINT FK_DeptSetting_Company
        FOREIGN KEY (CompanyId) REFERENCES Companies (Id)
        ON UPDATE CASCADE ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS IX_Template_Company          ON PerformanceTemplates (CompanyId, IsCurrent);
CREATE INDEX IF NOT EXISTS IX_Template_Group            ON PerformanceTemplates (GroupKey);
CREATE INDEX IF NOT EXISTS IX_TemplateCriterion_Template ON PerformanceTemplateCriteria (TemplateId);
CREATE INDEX IF NOT EXISTS IX_Cycle_Company             ON PerformanceCycles (CompanyId);
CREATE INDEX IF NOT EXISTS IX_Goal_Employee             ON PerformanceGoals (EmployeeId);
CREATE INDEX IF NOT EXISTS IX_Career_Employee           ON PerformanceCareerEvents (EmployeeId);
CREATE INDEX IF NOT EXISTS IX_Review_Cycle              ON PerformanceReviews (CycleId);
CREATE INDEX IF NOT EXISTS IX_DeptSetting_Company       ON PerformanceDeptSettings (CompanyId);

-- ----------------------------------------------------------------------------
--  Built-in template library (CompanyId NULL). Shipped ready to use; HR duplicates
--  one to create a company-specific version, leaving these untouched as a fallback.
--  Weights are percentages that sum to 100 per template.
-- ----------------------------------------------------------------------------

INSERT INTO PerformanceTemplates (CompanyId, GroupKey, Version, IsCurrent, Kind, Name, Description, DepartmentTag, ScaleMax, IsBuiltIn, IsArchived, CreatedAtUtc)
VALUES (NULL, 'builtin-general', 1, 1, 1, 'Évaluation générale', 'Critères universels applicables à tout employé de l''entreprise.', NULL, '20', 1, 0, datetime('now'));
INSERT INTO PerformanceTemplateCriteria (TemplateId, Label, WeightPercent, SortOrder) VALUES
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-general' AND Version=1), 'Ponctualité et assiduité', '20', 0),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-general' AND Version=1), 'Fiabilité', '20', 1),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-general' AND Version=1), 'Travail d''équipe', '20', 2),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-general' AND Version=1), 'Respect du règlement intérieur', '20', 3),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-general' AND Version=1), 'Communication', '20', 4);

INSERT INTO PerformanceTemplates (CompanyId, GroupKey, Version, IsCurrent, Kind, Name, Description, DepartmentTag, ScaleMax, IsBuiltIn, IsArchived, CreatedAtUtc)
VALUES (NULL, 'builtin-sales', 1, 1, 2, 'Département commercial', 'Évaluation orientée résultats commerciaux et relation client.', 'Commercial', '20', 1, 0, datetime('now'));
INSERT INTO PerformanceTemplateCriteria (TemplateId, Label, WeightPercent, SortOrder) VALUES
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-sales' AND Version=1), 'Objectifs de vente atteints', '35', 0),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-sales' AND Version=1), 'Fidélisation des clients', '20', 1),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-sales' AND Version=1), 'Taux de conclusion des affaires', '25', 2),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-sales' AND Version=1), 'Ventes additionnelles (upsell)', '20', 3);

INSERT INTO PerformanceTemplates (CompanyId, GroupKey, Version, IsCurrent, Kind, Name, Description, DepartmentTag, ScaleMax, IsBuiltIn, IsArchived, CreatedAtUtc)
VALUES (NULL, 'builtin-production', 1, 1, 3, 'Département production', 'Évaluation orientée qualité, sécurité et respect du planning.', 'Production', '20', 1, 0, datetime('now'));
INSERT INTO PerformanceTemplateCriteria (TemplateId, Label, WeightPercent, SortOrder) VALUES
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-production' AND Version=1), 'Taux de qualité de production', '30', 0),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-production' AND Version=1), 'Respect des consignes de sécurité', '25', 1),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-production' AND Version=1), 'Respect du planning de production', '25', 2),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-production' AND Version=1), 'Entretien des équipements', '20', 3);

INSERT INTO PerformanceTemplates (CompanyId, GroupKey, Version, IsCurrent, Kind, Name, Description, DepartmentTag, ScaleMax, IsBuiltIn, IsArchived, CreatedAtUtc)
VALUES (NULL, 'builtin-admin', 1, 1, 4, 'Département administratif', 'Évaluation orientée exactitude, réactivité et respect des procédures.', 'Administration', '20', 1, 0, datetime('now'));
INSERT INTO PerformanceTemplateCriteria (TemplateId, Label, WeightPercent, SortOrder) VALUES
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-admin' AND Version=1), 'Exactitude des tâches', '30', 0),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-admin' AND Version=1), 'Réactivité', '25', 1),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-admin' AND Version=1), 'Qualité de la documentation', '20', 2),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-admin' AND Version=1), 'Respect des procédures', '25', 3);

INSERT INTO PerformanceTemplates (CompanyId, GroupKey, Version, IsCurrent, Kind, Name, Description, DepartmentTag, ScaleMax, IsBuiltIn, IsArchived, CreatedAtUtc)
VALUES (NULL, 'builtin-technical', 1, 1, 5, 'Département technique / IT', 'Évaluation orientée délais, qualité technique et résolution.', 'Technique', '20', 1, 0, datetime('now'));
INSERT INTO PerformanceTemplateCriteria (TemplateId, Label, WeightPercent, SortOrder) VALUES
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-technical' AND Version=1), 'Respect des délais de projet', '30', 0),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-technical' AND Version=1), 'Qualité du travail / du code', '30', 1),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-technical' AND Version=1), 'Rapidité de résolution des problèmes', '20', 2),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-technical' AND Version=1), 'Évolution des compétences techniques', '20', 3);

INSERT INTO PerformanceTemplates (CompanyId, GroupKey, Version, IsCurrent, Kind, Name, Description, DepartmentTag, ScaleMax, IsBuiltIn, IsArchived, CreatedAtUtc)
VALUES (NULL, 'builtin-management', 1, 1, 6, 'Encadrement / chef d''équipe', 'Évaluation orientée performance d''équipe et développement du personnel.', 'Direction', '20', 1, 0, datetime('now'));
INSERT INTO PerformanceTemplateCriteria (TemplateId, Label, WeightPercent, SortOrder) VALUES
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-management' AND Version=1), 'Performance de l''équipe', '30', 0),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-management' AND Version=1), 'Efficacité de la délégation', '20', 1),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-management' AND Version=1), 'Développement du personnel', '25', 2),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-management' AND Version=1), 'Qualité des décisions', '25', 3);

INSERT INTO PerformanceTemplates (CompanyId, GroupKey, Version, IsCurrent, Kind, Name, Description, DepartmentTag, ScaleMax, IsBuiltIn, IsArchived, CreatedAtUtc)
VALUES (NULL, 'builtin-probation', 1, 1, 7, 'Fin de période d''essai', 'Évaluation courte pour valider un nouvel employé en fin d''essai.', NULL, '20', 1, 0, datetime('now'));
INSERT INTO PerformanceTemplateCriteria (TemplateId, Label, WeightPercent, SortOrder) VALUES
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-probation' AND Version=1), 'Intégration et adaptation', '25', 0),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-probation' AND Version=1), 'Maîtrise du poste', '30', 1),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-probation' AND Version=1), 'Assiduité et ponctualité', '20', 2),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='builtin-probation' AND Version=1), 'Comportement professionnel', '25', 3);
