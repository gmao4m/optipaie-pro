-- ============================================================================
--  OptiPaie PRO - Migration 0038 : force-change-password flag on local users
-- ----------------------------------------------------------------------------
--  Additive. Lets an administrator reset a user's password to a temporary one
--  that the user MUST replace at their next login (MustChangePassword = 1), so a
--  reset credential never becomes a lasting one. Default 0 = no change required,
--  so every existing account keeps logging in exactly as before. Dormant until
--  the optional login gate is enabled. Nothing here touches payroll.
-- ============================================================================

ALTER TABLE Users ADD COLUMN MustChangePassword INTEGER NOT NULL DEFAULT 0;
