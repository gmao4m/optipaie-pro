-- ----------------------------------------------------------------------------
--  Migration 0027 : Evaluation rebuild — KPI criterion type + department grids
--
--  Additive. Adds a criterion TYPE (behavioral vs KPI/numeric) to both the
--  template criteria and the review criteria, plus the KPI target/achieved values
--  on a review line. Then seeds five department-anchored built-in templates on a
--  1-5 scale (the evaluation grids the rebuild centres on). Existing /20 templates
--  and every scored review stay valid and untouched.
--
--  Convention (as elsewhere): decimals stored as invariant-culture TEXT,
--  bools as INTEGER with a 0/1 CHECK.
-- ----------------------------------------------------------------------------

-- 1 = Behavioral (rated 1-5 directly), 2 = KPI (target + achieved -> 1-5)
ALTER TABLE PerformanceTemplateCriteria ADD COLUMN CriterionType  INTEGER NOT NULL DEFAULT 1;
ALTER TABLE PerformanceTemplateCriteria ADD COLUMN HigherIsBetter INTEGER NOT NULL DEFAULT 1 CHECK (HigherIsBetter IN (0, 1));
ALTER TABLE PerformanceTemplateCriteria ADD COLUMN DefaultTarget  TEXT    NULL;

ALTER TABLE PerformanceCriteria ADD COLUMN CriterionType  INTEGER NOT NULL DEFAULT 1;
ALTER TABLE PerformanceCriteria ADD COLUMN HigherIsBetter INTEGER NOT NULL DEFAULT 1 CHECK (HigherIsBetter IN (0, 1));
ALTER TABLE PerformanceCriteria ADD COLUMN KpiTarget      TEXT    NULL;
ALTER TABLE PerformanceCriteria ADD COLUMN KpiAchieved    TEXT    NULL;

-- ----------------------------------------------------------------------------
--  Department evaluation grids (CompanyId NULL, 1-5 scale). One per default
--  department; "Générale" is the fallback for any department without its own grid.
--  Weights are percentages summing to 100. CriterionType 2 = KPI.
-- ----------------------------------------------------------------------------

-- Production ----------------------------------------------------------------
INSERT INTO PerformanceTemplates (CompanyId, GroupKey, Version, IsCurrent, Kind, Name, Description, DepartmentTag, ScaleMax, IsBuiltIn, IsArchived, CreatedAtUtc)
VALUES (NULL, 'dept-production', 1, 1, 3, 'Grille — Production', 'Qualité, productivité, sécurité et assiduité.', 'Production', '5', 1, 0, datetime('now'));
INSERT INTO PerformanceTemplateCriteria (TemplateId, Label, WeightPercent, CriterionType, HigherIsBetter, DefaultTarget, SortOrder) VALUES
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-production' AND Version=1), 'Qualité du travail', '35', 1, 1, NULL, 0),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-production' AND Version=1), 'Productivité (pièces / objectif)', '25', 2, 1, NULL, 1),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-production' AND Version=1), 'Respect des consignes de sécurité', '25', 1, 1, NULL, 2),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-production' AND Version=1), 'Assiduité', '15', 1, 1, NULL, 3);

-- Commercial ----------------------------------------------------------------
INSERT INTO PerformanceTemplates (CompanyId, GroupKey, Version, IsCurrent, Kind, Name, Description, DepartmentTag, ScaleMax, IsBuiltIn, IsArchived, CreatedAtUtc)
VALUES (NULL, 'dept-commercial', 1, 1, 2, 'Grille — Commercial', 'Objectifs de vente, relation client et esprit d''équipe.', 'Commercial', '5', 1, 0, datetime('now'));
INSERT INTO PerformanceTemplateCriteria (TemplateId, Label, WeightPercent, CriterionType, HigherIsBetter, DefaultTarget, SortOrder) VALUES
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-commercial' AND Version=1), 'Objectifs de vente (réalisé / cible)', '50', 2, 1, NULL, 0),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-commercial' AND Version=1), 'Relation client', '20', 1, 1, NULL, 1),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-commercial' AND Version=1), 'Assiduité', '15', 1, 1, NULL, 2),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-commercial' AND Version=1), 'Esprit d''équipe', '15', 1, 1, NULL, 3);

-- Administration ------------------------------------------------------------
INSERT INTO PerformanceTemplates (CompanyId, GroupKey, Version, IsCurrent, Kind, Name, Description, DepartmentTag, ScaleMax, IsBuiltIn, IsArchived, CreatedAtUtc)
VALUES (NULL, 'dept-administration', 1, 1, 4, 'Grille — Administration', 'Exactitude, respect des délais, réactivité et assiduité.', 'Administration', '5', 1, 0, datetime('now'));
INSERT INTO PerformanceTemplateCriteria (TemplateId, Label, WeightPercent, CriterionType, HigherIsBetter, DefaultTarget, SortOrder) VALUES
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-administration' AND Version=1), 'Exactitude des tâches', '35', 1, 1, NULL, 0),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-administration' AND Version=1), 'Respect des délais (% dans les temps)', '25', 2, 1, '100', 1),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-administration' AND Version=1), 'Réactivité', '20', 1, 1, NULL, 2),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-administration' AND Version=1), 'Assiduité', '20', 1, 1, NULL, 3);

-- Informatique --------------------------------------------------------------
INSERT INTO PerformanceTemplates (CompanyId, GroupKey, Version, IsCurrent, Kind, Name, Description, DepartmentTag, ScaleMax, IsBuiltIn, IsArchived, CreatedAtUtc)
VALUES (NULL, 'dept-informatique', 1, 1, 5, 'Grille — Informatique', 'Qualité du code, résolution des incidents, délais et compétences.', 'Informatique', '5', 1, 0, datetime('now'));
INSERT INTO PerformanceTemplateCriteria (TemplateId, Label, WeightPercent, CriterionType, HigherIsBetter, DefaultTarget, SortOrder) VALUES
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-informatique' AND Version=1), 'Qualité du travail / du code', '35', 1, 1, NULL, 0),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-informatique' AND Version=1), 'Incidents résolus (réalisé / cible)', '25', 2, 1, NULL, 1),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-informatique' AND Version=1), 'Respect des délais de projet', '20', 1, 1, NULL, 2),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-informatique' AND Version=1), 'Évolution des compétences', '20', 1, 1, NULL, 3);

-- Générale (fallback) -------------------------------------------------------
INSERT INTO PerformanceTemplates (CompanyId, GroupKey, Version, IsCurrent, Kind, Name, Description, DepartmentTag, ScaleMax, IsBuiltIn, IsArchived, CreatedAtUtc)
VALUES (NULL, 'dept-generale', 1, 1, 1, 'Grille — Générale', 'Critères universels — grille par défaut de tout département.', 'Générale', '5', 1, 0, datetime('now'));
INSERT INTO PerformanceTemplateCriteria (TemplateId, Label, WeightPercent, CriterionType, HigherIsBetter, DefaultTarget, SortOrder) VALUES
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-generale' AND Version=1), 'Assiduité et ponctualité', '25', 1, 1, NULL, 0),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-generale' AND Version=1), 'Fiabilité', '25', 1, 1, NULL, 1),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-generale' AND Version=1), 'Travail d''équipe', '25', 1, 1, NULL, 2),
 ((SELECT Id FROM PerformanceTemplates WHERE GroupKey='dept-generale' AND Version=1), 'Communication', '25', 1, 1, NULL, 3);
