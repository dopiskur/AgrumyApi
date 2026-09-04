-- Roadmap #204: RBAC consolidation. UserAddAsync/UserUpdateAsync used to write ONLY
-- user.UserGroupID, never userUserRole - so any account created/edited before this fix could be
-- relying solely on the legacy fallback in ResolveCallerTokenRolesAsync. This backfills a
-- userUserRole row for every such account from its current UserGroupID -> userGroup.UserRoleID
-- mapping, so the fallback is no longer load-bearing for anyone going forward.
--
-- SAFE TO RE-RUN: WHERE NOT EXISTS skips any user who already has a userUserRole row (including
-- everyone created through the new role-checkbox Create/Edit forms, or already migrated).

INSERT INTO userUserRole (UserID, UserRoleID)
SELECT u.IDUser, g.UserRoleID
FROM `user` u
JOIN `userGroup` g ON u.UserGroupID = g.IDUserGroup
WHERE g.UserRoleID IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM userUserRole x WHERE x.UserID = u.IDUser);

-- Sanity check after running:
--   SELECT COUNT(*) FROM `user` u WHERE NOT EXISTS (SELECT 1 FROM userUserRole x WHERE x.UserID = u.IDUser);
--   -- remaining count = users with no UserGroupID mapping either (e.g. bootstrap admin already has
--   -- its own userUserRole row from seeding, so it will not appear here)
