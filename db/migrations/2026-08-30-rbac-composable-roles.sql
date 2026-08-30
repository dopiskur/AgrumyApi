-- Roadmap #66: composable roles - a user can hold several roles at once (many-to-many), replacing
-- the pre-#66 model where UserGroupID picked exactly one role.
--
-- WHY THIS IS MANUAL: see 2026-08-30-event-log-columns.sql - EnsureSchemaAsync() only provisions a
-- brand-new (zero-table) database, never alters an existing table or seeds new reference rows into
-- one that already has data. Run this by hand against each database that predates this change.
--
-- SAFE TO RE-RUN: every INSERT is guarded by a NOT EXISTS check; CREATE TABLE uses IF NOT EXISTS.

-- 1. New role scope: "tenant", alongside the existing "global" (IDRoleScope 1001).
INSERT INTO `userRoleScope` (`RoleScopeName`)
SELECT 'tenant' WHERE NOT EXISTS (SELECT 1 FROM `userRoleScope` WHERE `RoleScopeName` = 'tenant');

-- 2. The 8 new roles (api.Security.RoleNames.All) - looked up by name everywhere in code, so their
-- actual generated IDUserRole values don't matter and are never hardcoded here.
INSERT INTO `userRole` (`RoleName`, `RoleScopeID`)
SELECT 'Global admin', (SELECT IDRoleScope FROM `userRoleScope` WHERE RoleScopeName = 'global')
WHERE NOT EXISTS (SELECT 1 FROM `userRole` WHERE RoleName = 'Global admin');

INSERT INTO `userRole` (`RoleName`, `RoleScopeID`)
SELECT 'Global reader', (SELECT IDRoleScope FROM `userRoleScope` WHERE RoleScopeName = 'global')
WHERE NOT EXISTS (SELECT 1 FROM `userRole` WHERE RoleName = 'Global reader');

INSERT INTO `userRole` (`RoleName`, `RoleScopeID`)
SELECT 'Global User', (SELECT IDRoleScope FROM `userRoleScope` WHERE RoleScopeName = 'global')
WHERE NOT EXISTS (SELECT 1 FROM `userRole` WHERE RoleName = 'Global User');

INSERT INTO `userRole` (`RoleName`, `RoleScopeID`)
SELECT 'Global Device', (SELECT IDRoleScope FROM `userRoleScope` WHERE RoleScopeName = 'global')
WHERE NOT EXISTS (SELECT 1 FROM `userRole` WHERE RoleName = 'Global Device');

INSERT INTO `userRole` (`RoleName`, `RoleScopeID`)
SELECT 'Tenant admin', (SELECT IDRoleScope FROM `userRoleScope` WHERE RoleScopeName = 'tenant')
WHERE NOT EXISTS (SELECT 1 FROM `userRole` WHERE RoleName = 'Tenant admin');

INSERT INTO `userRole` (`RoleName`, `RoleScopeID`)
SELECT 'Tenant reader', (SELECT IDRoleScope FROM `userRoleScope` WHERE RoleScopeName = 'tenant')
WHERE NOT EXISTS (SELECT 1 FROM `userRole` WHERE RoleName = 'Tenant reader');

INSERT INTO `userRole` (`RoleName`, `RoleScopeID`)
SELECT 'Tenant User', (SELECT IDRoleScope FROM `userRoleScope` WHERE RoleScopeName = 'tenant')
WHERE NOT EXISTS (SELECT 1 FROM `userRole` WHERE RoleName = 'Tenant User');

INSERT INTO `userRole` (`RoleName`, `RoleScopeID`)
SELECT 'Tenant Device', (SELECT IDRoleScope FROM `userRoleScope` WHERE RoleScopeName = 'tenant')
WHERE NOT EXISTS (SELECT 1 FROM `userRole` WHERE RoleName = 'Tenant Device');

-- 3. The many-to-many junction table itself.
CREATE TABLE IF NOT EXISTS `userUserRole` (
  `UserID` INT NOT NULL,
  `UserRoleID` INT NOT NULL,
  PRIMARY KEY (`UserID`, `UserRoleID`),
  CONSTRAINT `fk_userUserRole_user` FOREIGN KEY (`UserID`) REFERENCES `user` (`IDUser`) ON DELETE CASCADE,
  CONSTRAINT `fk_userUserRole_userRole` FOREIGN KEY (`UserRoleID`) REFERENCES `userRole` (`IDUserRole`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 4. Migrate every existing account onto the new model (agreed mapping, see roadmap #66):
--    admin @ TenantID==0    -> Global admin  (nobody above them to ask, formalizes roadmap #65)
--    admin @ any other tenant -> Tenant admin
--    plain "user", any tenant -> Tenant reader (view-only default, unchanged behaviour - #66 does
--    NOT retroactively grant existing "user" accounts any management power)
INSERT INTO `userUserRole` (`UserID`, `UserRoleID`)
SELECT u.`IDUser`, (SELECT IDUserRole FROM `userRole` WHERE RoleName = 'Global admin')
FROM `user` u
JOIN `userGroup` g ON u.`UserGroupID` = g.`IDUserGroup`
JOIN `userRole` r ON g.`UserRoleID` = r.`IDUserRole`
WHERE r.`RoleName` = 'admin' AND u.`TenantID` = 0
  AND NOT EXISTS (
    SELECT 1 FROM `userUserRole` x
    WHERE x.UserID = u.IDUser AND x.UserRoleID = (SELECT IDUserRole FROM `userRole` WHERE RoleName = 'Global admin')
  );

INSERT INTO `userUserRole` (`UserID`, `UserRoleID`)
SELECT u.`IDUser`, (SELECT IDUserRole FROM `userRole` WHERE RoleName = 'Tenant admin')
FROM `user` u
JOIN `userGroup` g ON u.`UserGroupID` = g.`IDUserGroup`
JOIN `userRole` r ON g.`UserRoleID` = r.`IDUserRole`
WHERE r.`RoleName` = 'admin' AND u.`TenantID` <> 0
  AND NOT EXISTS (
    SELECT 1 FROM `userUserRole` x
    WHERE x.UserID = u.IDUser AND x.UserRoleID = (SELECT IDUserRole FROM `userRole` WHERE RoleName = 'Tenant admin')
  );

INSERT INTO `userUserRole` (`UserID`, `UserRoleID`)
SELECT u.`IDUser`, (SELECT IDUserRole FROM `userRole` WHERE RoleName = 'Tenant reader')
FROM `user` u
JOIN `userGroup` g ON u.`UserGroupID` = g.`IDUserGroup`
JOIN `userRole` r ON g.`UserRoleID` = r.`IDUserRole`
WHERE r.`RoleName` = 'user'
  AND NOT EXISTS (
    SELECT 1 FROM `userUserRole` x
    WHERE x.UserID = u.IDUser AND x.UserRoleID = (SELECT IDUserRole FROM `userRole` WHERE RoleName = 'Tenant reader')
  );

-- Sanity check after running:
--   SELECT RoleName, RoleScopeID FROM userRole;                    -- expect 10 rows total (2 legacy + 8 new)
--   SELECT COUNT(*) FROM userUserRole;                             -- expect one row per existing user
--   SELECT u.Email, r.RoleName FROM user u
--     JOIN userUserRole x ON x.UserID = u.IDUser
--     JOIN userRole r ON r.IDUserRole = x.UserRoleID;               -- spot-check the mapping
