-- ============================================================================
--  OptiPaie PRO - Migration 0031 : configurable leave types (Types de congé)
-- ----------------------------------------------------------------------------
--  PURELY ADDITIVE and retro-compatible with every existing client database.
--
--  Why a table + a nullable link (not a wider CHECK): the production schema puts
--  CHECK (Type IN 1..5) on LeaveRequests.Type; SQLite cannot drop a CHECK without
--  rebuilding the table (destructive). So we DO NOT touch Type. Instead:
--    * LeaveTypes             = the configurable catalogue (label, payment category,
--                               decrements-balance, legal duration, active…).
--    * LeaveRequests.LeaveTypeId (NULLABLE, added below) = optional link to a type.
--  A request maps onto the legacy Type column via LeaveTypes.BaseType (still 1..5),
--  so the existing CHECK stays satisfied. Every PRE-EXISTING request keeps
--  LeaveTypeId = NULL and is read EXACTLY as before (legacy LeaveType + policy) —
--  balances are unchanged.
-- ============================================================================

CREATE TABLE IF NOT EXISTS LeaveTypes (
    Id                      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId               INTEGER,                                   -- NULL = global default (all companies)
    Code                    TEXT    NOT NULL,
    LabelAr                 TEXT    NOT NULL,
    LabelFr                 TEXT,
    BaseType                INTEGER NOT NULL CHECK (BaseType IN (1, 2, 3, 4, 5)),
    PaymentCategory         INTEGER NOT NULL DEFAULT 1 CHECK (PaymentCategory IN (1, 2, 3)),
    DecrementsAnnualBalance INTEGER NOT NULL DEFAULT 0 CHECK (DecrementsAnnualBalance IN (0, 1)),
    LegalDurationDays       TEXT,                                      -- invariant decimal, nullable
    OncePerCareer           INTEGER NOT NULL DEFAULT 0 CHECK (OncePerCareer IN (0, 1)),
    IsActive                INTEGER NOT NULL DEFAULT 1 CHECK (IsActive IN (0, 1)),
    SortOrder               INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc            TEXT    NOT NULL,
    UpdatedAtUtc            TEXT,
    IsDeleted               INTEGER NOT NULL DEFAULT 0 CHECK (IsDeleted IN (0, 1))
);

CREATE INDEX IF NOT EXISTS IX_LeaveTypes_Company ON LeaveTypes (CompanyId);

-- Additive, nullable link from a request to a configurable type.
ALTER TABLE LeaveRequests ADD COLUMN LeaveTypeId INTEGER;
CREATE INDEX IF NOT EXISTS IX_Leave_TypeId ON LeaveRequests (LeaveTypeId);

-- ---------------------------------------------------------------------------
--  Seed the default GLOBAL catalogue (CompanyId NULL), conform to loi 90-11 /
--  loi 83-11 research. Idempotent (guarded on Code + global scope). Seeding these
--  rows does NOT change any existing balance: pre-existing requests do not point
--  to them (LeaveTypeId stays NULL). PaymentCategory: 1=employeur, 2=CNAS, 3=sans solde.
-- ---------------------------------------------------------------------------
INSERT INTO LeaveTypes (CompanyId, Code, LabelAr, LabelFr, BaseType, PaymentCategory, DecrementsAnnualBalance, LegalDurationDays, OncePerCareer, IsActive, SortOrder, CreatedAtUtc)
SELECT NULL, 'ANNUAL', 'العطلة السنوية', 'Congé annuel', 1, 1, 1, NULL, 0, 1, 1, '2026-01-01T00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM LeaveTypes WHERE Code = 'ANNUAL' AND CompanyId IS NULL);

INSERT INTO LeaveTypes (CompanyId, Code, LabelAr, LabelFr, BaseType, PaymentCategory, DecrementsAnnualBalance, LegalDurationDays, OncePerCareer, IsActive, SortOrder, CreatedAtUtc)
SELECT NULL, 'SICK', 'عطلة مرضية', 'Congé de maladie', 2, 2, 0, NULL, 0, 1, 2, '2026-01-01T00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM LeaveTypes WHERE Code = 'SICK' AND CompanyId IS NULL);

INSERT INTO LeaveTypes (CompanyId, Code, LabelAr, LabelFr, BaseType, PaymentCategory, DecrementsAnnualBalance, LegalDurationDays, OncePerCareer, IsActive, SortOrder, CreatedAtUtc)
SELECT NULL, 'MATERNITY', 'عطلة الأمومة', 'Congé de maternité', 4, 2, 0, '150', 0, 1, 3, '2026-01-01T00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM LeaveTypes WHERE Code = 'MATERNITY' AND CompanyId IS NULL);

INSERT INTO LeaveTypes (CompanyId, Code, LabelAr, LabelFr, BaseType, PaymentCategory, DecrementsAnnualBalance, LegalDurationDays, OncePerCareer, IsActive, SortOrder, CreatedAtUtc)
SELECT NULL, 'UNPAID', 'عطلة بدون أجر', 'Congé sans solde', 3, 3, 0, NULL, 0, 1, 4, '2026-01-01T00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM LeaveTypes WHERE Code = 'UNPAID' AND CompanyId IS NULL);

INSERT INTO LeaveTypes (CompanyId, Code, LabelAr, LabelFr, BaseType, PaymentCategory, DecrementsAnnualBalance, LegalDurationDays, OncePerCareer, IsActive, SortOrder, CreatedAtUtc)
SELECT NULL, 'FAMILY_MARRIAGE', 'زواج العامل', 'Mariage du travailleur', 5, 1, 0, '3', 0, 1, 10, '2026-01-01T00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM LeaveTypes WHERE Code = 'FAMILY_MARRIAGE' AND CompanyId IS NULL);

INSERT INTO LeaveTypes (CompanyId, Code, LabelAr, LabelFr, BaseType, PaymentCategory, DecrementsAnnualBalance, LegalDurationDays, OncePerCareer, IsActive, SortOrder, CreatedAtUtc)
SELECT NULL, 'FAMILY_BIRTH', 'ازدياد مولود', 'Naissance d''un enfant', 5, 1, 0, '3', 0, 1, 11, '2026-01-01T00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM LeaveTypes WHERE Code = 'FAMILY_BIRTH' AND CompanyId IS NULL);

INSERT INTO LeaveTypes (CompanyId, Code, LabelAr, LabelFr, BaseType, PaymentCategory, DecrementsAnnualBalance, LegalDurationDays, OncePerCareer, IsActive, SortOrder, CreatedAtUtc)
SELECT NULL, 'FAMILY_CHILD_MARRIAGE', 'زواج أحد الأبناء', 'Mariage d''un descendant', 5, 1, 0, '3', 0, 1, 12, '2026-01-01T00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM LeaveTypes WHERE Code = 'FAMILY_CHILD_MARRIAGE' AND CompanyId IS NULL);

INSERT INTO LeaveTypes (CompanyId, Code, LabelAr, LabelFr, BaseType, PaymentCategory, DecrementsAnnualBalance, LegalDurationDays, OncePerCareer, IsActive, SortOrder, CreatedAtUtc)
SELECT NULL, 'FAMILY_DEATH_RELATIVE', 'وفاة أصل أو فرع أو قريب من الدرجة الأولى', 'Décès ascendant/descendant/collatéral 1er degré', 5, 1, 0, '3', 0, 1, 13, '2026-01-01T00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM LeaveTypes WHERE Code = 'FAMILY_DEATH_RELATIVE' AND CompanyId IS NULL);

INSERT INTO LeaveTypes (CompanyId, Code, LabelAr, LabelFr, BaseType, PaymentCategory, DecrementsAnnualBalance, LegalDurationDays, OncePerCareer, IsActive, SortOrder, CreatedAtUtc)
SELECT NULL, 'FAMILY_DEATH_SPOUSE', 'وفاة الزوج', 'Décès du conjoint', 5, 1, 0, '3', 0, 1, 14, '2026-01-01T00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM LeaveTypes WHERE Code = 'FAMILY_DEATH_SPOUSE' AND CompanyId IS NULL);

INSERT INTO LeaveTypes (CompanyId, Code, LabelAr, LabelFr, BaseType, PaymentCategory, DecrementsAnnualBalance, LegalDurationDays, OncePerCareer, IsActive, SortOrder, CreatedAtUtc)
SELECT NULL, 'FAMILY_CIRCUMCISION', 'ختان أحد الأبناء', 'Circoncision d''un enfant', 5, 1, 0, '3', 0, 1, 15, '2026-01-01T00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM LeaveTypes WHERE Code = 'FAMILY_CIRCUMCISION' AND CompanyId IS NULL);

INSERT INTO LeaveTypes (CompanyId, Code, LabelAr, LabelFr, BaseType, PaymentCategory, DecrementsAnnualBalance, LegalDurationDays, OncePerCareer, IsActive, SortOrder, CreatedAtUtc)
SELECT NULL, 'PILGRIMAGE', 'الحج', 'Pèlerinage (Hadj)', 5, 1, 0, NULL, 1, 1, 20, '2026-01-01T00:00:00'
WHERE NOT EXISTS (SELECT 1 FROM LeaveTypes WHERE Code = 'PILGRIMAGE' AND CompanyId IS NULL);
