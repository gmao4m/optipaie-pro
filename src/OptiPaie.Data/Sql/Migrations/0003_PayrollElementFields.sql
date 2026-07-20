-- ============================================================================
--  OptiPaie DZ - Migration 0003 : additional payroll element fields
-- ----------------------------------------------------------------------------
--  Adds the element attributes required by the payroll element engine:
--    InternalCode, IsPrintable, IncludedInLissage, IsAutomatic.
--  Additive and non-destructive: existing rows receive sensible defaults.
-- ============================================================================

ALTER TABLE PayrollElements ADD COLUMN InternalCode      TEXT;
ALTER TABLE PayrollElements ADD COLUMN IsPrintable       INTEGER NOT NULL DEFAULT 1;
ALTER TABLE PayrollElements ADD COLUMN IncludedInLissage INTEGER NOT NULL DEFAULT 0;
ALTER TABLE PayrollElements ADD COLUMN IsAutomatic       INTEGER NOT NULL DEFAULT 0;

-- Non-monthly elements (periodicity <> Monthly) participate in IRG lissage.
UPDATE PayrollElements SET IncludedInLissage = 1 WHERE Periodicity <> 1;

-- Stable internal codes for the seeded system elements.
UPDATE PayrollElements SET InternalCode = 'IEP'             WHERE NameFr = 'Indemnité d''Expérience Professionnelle';
UPDATE PayrollElements SET InternalCode = 'PRIME_RENDEMENT' WHERE NameFr = 'Prime de Rendement';
UPDATE PayrollElements SET InternalCode = 'PRIME_RESP'      WHERE NameFr = 'Prime de Responsabilité';
UPDATE PayrollElements SET InternalCode = 'PRIME_RISQUE'    WHERE NameFr = 'Prime de Risque / Nuisance';
UPDATE PayrollElements SET InternalCode = 'PRIME_NUIT'      WHERE NameFr = 'Prime de Nuit';
UPDATE PayrollElements SET InternalCode = 'HEURES_SUP'      WHERE NameFr = 'Heures Supplémentaires';
UPDATE PayrollElements SET InternalCode = 'PRIME_PANIER'    WHERE NameFr = 'Prime de Panier';
UPDATE PayrollElements SET InternalCode = 'PRIME_TRANSPORT' WHERE NameFr = 'Prime de Transport';
UPDATE PayrollElements SET InternalCode = 'CONGE_PAYE'      WHERE NameFr = 'Indemnité de Congé Payé';
UPDATE PayrollElements SET InternalCode = 'RAPPEL'          WHERE NameFr = 'Rappel';
UPDATE PayrollElements SET InternalCode = 'PRIME_EXCEP'     WHERE NameFr = 'Prime Exceptionnelle';
UPDATE PayrollElements SET InternalCode = 'RET_ABSENCE'     WHERE NameFr = 'Retenue Absence';
UPDATE PayrollElements SET InternalCode = 'ACOMPTE'         WHERE NameFr = 'Acompte';
UPDATE PayrollElements SET InternalCode = 'AVANCE'          WHERE NameFr = 'Avance';
