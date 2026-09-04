-- New `auditLog` table - write-once admin-action trail (who changed another account's
-- access/state, when, to what).
--
-- WHY THIS IS MANUAL:
-- EnsureSchemaAsync() (Agrumy.Api's startup schema check) calls EnsureCreatedAsync(), which only
-- provisions a database that has ZERO tables - it never adds a table to a database that already
-- has others. Run this by hand against each such database before deploying this code.
--
-- SAFE TO RE-RUN: guarded with IF NOT EXISTS.

CREATE TABLE IF NOT EXISTS `auditLog` (
  `IDAuditLog` INT NOT NULL AUTO_INCREMENT,
  `TimestampUtc` DATETIME(6) NOT NULL,
  `TenantID` INT DEFAULT NULL,
  `ActorUserID` INT DEFAULT NULL,
  `ActorEmail` VARCHAR(255) DEFAULT NULL,
  `Action` VARCHAR(100) NOT NULL,
  `TargetType` VARCHAR(50) DEFAULT NULL,
  `TargetId` VARCHAR(50) DEFAULT NULL,
  `Details` TEXT DEFAULT NULL,
  PRIMARY KEY (`IDAuditLog`),
  KEY `ix_auditLog_tenant_timestamp` (`TenantID`, `TimestampUtc`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Sanity check after running:
--   SHOW CREATE TABLE `auditLog`;
