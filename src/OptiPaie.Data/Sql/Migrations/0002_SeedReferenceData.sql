-- ============================================================================
--  OptiPaie DZ - Migration 0002 : Seed reference data
-- ----------------------------------------------------------------------------
--  * Supported languages (fr / ar)
--  * Default application settings
--  * Legal parameters in force for 2026 (CNAS 9% / 26%, SNMG 24 000)
--  * Default Algerian payroll elements (system, bilingual)
--
--  Enum integer values used below:
--    ElementType        : 1=Gain        2=Deduction
--    CalculationMethod  : 1=FixedAmount 2=Percentage 3=QtyUnitPrice 4=BaseRate
--    CalculationBase    : 1=SalaireDeBase 2=SalaireBrut 3=BaseCotisable
--    Periodicity        : 1=Monthly 2=Quarterly 3=Annual 4=OneOff
--    Direction          : 1=LeftToRight 2=RightToLeft
-- ============================================================================

-- --------------------------------------------------------------------------
--  Languages
-- --------------------------------------------------------------------------
INSERT INTO Languages (Code, NameNative, Direction, FontFamily, IsEnabled, DisplayOrder) VALUES
    ('fr', 'Français', 1, 'Segoe UI', 1, 1),
    ('ar', 'العربية',  2, 'Tahoma',   1, 2);

-- --------------------------------------------------------------------------
--  Application settings (defaults)
-- --------------------------------------------------------------------------
INSERT INTO AppSettings (SettingKey, SettingValue, CreatedAtUtc) VALUES
    ('LANGUAGE',            'fr',           strftime('%Y-%m-%d %H:%M:%S', 'now')),
    ('THEME',               'The Bezier',   strftime('%Y-%m-%d %H:%M:%S', 'now')),
    ('ROUNDING_SCALE',      '2',            strftime('%Y-%m-%d %H:%M:%S', 'now')),
    ('OVERTIME_MAJORATION', '0.5',          strftime('%Y-%m-%d %H:%M:%S', 'now'));

-- --------------------------------------------------------------------------
--  Legal parameters in force for 2026
--    (IRG barème / abattement / lissage are fixed in the engine, not here.)
-- --------------------------------------------------------------------------
INSERT INTO LegalParameters (ParamKey, ParamValue, EffectiveFrom, IsActive, Description, CreatedAtUtc) VALUES
    ('CNAS_EMPLOYEE_RATE', '0.09',  '2026-01-01', 1, 'Taux de cotisation CNAS - part salariale (9%)',  strftime('%Y-%m-%d %H:%M:%S', 'now')),
    ('CNAS_EMPLOYER_RATE', '0.26',  '2026-01-01', 1, 'Taux de cotisation CNAS - part patronale (26%)', strftime('%Y-%m-%d %H:%M:%S', 'now')),
    ('SNMG',               '24000', '2026-01-01', 1, 'Salaire National Minimum Garanti',               strftime('%Y-%m-%d %H:%M:%S', 'now'));

-- --------------------------------------------------------------------------
--  Default Algerian payroll elements (system catalog)
--    Columns: NameFr, NameAr, Description, ElementType, CalculationMethod,
--    CalculationBase, DefaultAmount, DefaultRate, DefaultQuantity,
--    DefaultUnitPrice, Periodicity, IsCnasApplicable, IsIrgApplicable,
--    IsIncludedInGross, ExemptionCeiling, IsEditable, IsEnabled, IsSystem,
--    DisplayOrder, CreatedAtUtc
--    (Salaire de base is intrinsic to the engine and is NOT a catalog element.)
-- --------------------------------------------------------------------------
INSERT INTO PayrollElements
    (NameFr, NameAr, Description, ElementType, CalculationMethod, CalculationBase,
     DefaultAmount, DefaultRate, DefaultQuantity, DefaultUnitPrice, Periodicity,
     IsCnasApplicable, IsIrgApplicable, IsIncludedInGross, ExemptionCeiling,
     IsEditable, IsEnabled, IsSystem, DisplayOrder, CreatedAtUtc)
VALUES
    -- Gains (cotisable + imposable)
    ('Indemnité d''Expérience Professionnelle', 'علاوة الخبرة المهنية', 'IEP / ancienneté : base × taux × années',
     1, 4, 1, NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, 1, 1, 1, 20, strftime('%Y-%m-%d %H:%M:%S', 'now')),

    ('Prime de Rendement', 'منحة المردودية', 'Pourcentage du salaire de base',
     1, 2, 1, NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, 1, 1, 1, 30, strftime('%Y-%m-%d %H:%M:%S', 'now')),

    ('Prime de Responsabilité', 'منحة المسؤولية', NULL,
     1, 1, NULL, NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, 1, 1, 1, 40, strftime('%Y-%m-%d %H:%M:%S', 'now')),

    ('Prime de Risque / Nuisance', 'منحة المخاطر', NULL,
     1, 1, NULL, NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, 1, 1, 1, 50, strftime('%Y-%m-%d %H:%M:%S', 'now')),

    ('Prime de Nuit', 'منحة العمل الليلي', 'Heures de nuit × taux horaire majoré',
     1, 3, NULL, NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, 1, 1, 1, 60, strftime('%Y-%m-%d %H:%M:%S', 'now')),

    ('Heures Supplémentaires', 'الساعات الإضافية', 'Heures × taux horaire majoré',
     1, 3, NULL, NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, 1, 1, 1, 70, strftime('%Y-%m-%d %H:%M:%S', 'now')),

    -- Gains (non cotisable / non imposable dans la limite - remboursement de frais)
    ('Prime de Panier', 'منحة السلة', 'Jours × montant journalier (exonérée dans la limite)',
     1, 3, NULL, NULL, NULL, NULL, NULL, 1, 0, 0, 1, NULL, 1, 1, 1, 80, strftime('%Y-%m-%d %H:%M:%S', 'now')),

    ('Prime de Transport', 'منحة النقل', 'Exonérée dans la limite',
     1, 1, NULL, NULL, NULL, NULL, NULL, 1, 0, 0, 1, NULL, 1, 1, 1, 90, strftime('%Y-%m-%d %H:%M:%S', 'now')),

    -- Gains périodiques / exceptionnels (soumis au lissage IRG)
    ('Indemnité de Congé Payé', 'بدل العطلة المدفوعة', NULL,
     1, 1, NULL, NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, 1, 1, 1, 100, strftime('%Y-%m-%d %H:%M:%S', 'now')),

    ('Rappel', 'استدراك', 'Rappel de salaire (IRG lissé sur la période concernée)',
     1, 1, NULL, NULL, NULL, NULL, NULL, 4, 1, 1, 1, NULL, 1, 1, 1, 110, strftime('%Y-%m-%d %H:%M:%S', 'now')),

    ('Prime Exceptionnelle', 'منحة استثنائية', NULL,
     1, 1, NULL, NULL, NULL, NULL, NULL, 4, 1, 1, 1, NULL, 1, 1, 1, 120, strftime('%Y-%m-%d %H:%M:%S', 'now')),

    -- Retenues qui réduisent l'assiette (cotisable + imposable)
    ('Retenue Absence', 'اقتطاع الغياب', 'Jours d''absence × taux journalier (réduit l''assiette)',
     2, 3, NULL, NULL, NULL, NULL, NULL, 1, 1, 1, 1, NULL, 1, 1, 1, 200, strftime('%Y-%m-%d %H:%M:%S', 'now')),

    -- Retenues nettes uniquement (n'affectent ni CNAS ni IRG)
    ('Acompte', 'تسبيق على الأجر', 'Acompte sur salaire (retenue nette uniquement)',
     2, 1, NULL, NULL, NULL, NULL, NULL, 1, 0, 0, 0, NULL, 1, 1, 1, 210, strftime('%Y-%m-%d %H:%M:%S', 'now')),

    ('Avance', 'سلفة', 'Avance (retenue nette uniquement)',
     2, 1, NULL, NULL, NULL, NULL, NULL, 1, 0, 0, 0, NULL, 1, 1, 1, 220, strftime('%Y-%m-%d %H:%M:%S', 'now'));
