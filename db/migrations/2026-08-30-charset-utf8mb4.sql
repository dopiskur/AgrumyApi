-- Roadmap #29 - charset unification (latin1 / utf8mb3 -> utf8mb4).
--
-- WHY THIS IS MANUAL:
-- The EF Core model sets no explicit charset, so a database built fresh by
-- Agrumy.Api's EnsureSchemaAsync() (EnsureCreatedAsync) already comes out utf8mb4 -
-- Pomelo applies an implicit HasCharSet("utf8mb4"). This is verified by the
-- Fresh_MySql_Schema_Is_Utf8mb4_Not_Latin1 integration test.
-- EnsureCreatedAsync() never touches a database that already has tables, so the
-- pre-EF `invent.hr` database (created in the SchemaScripts.cs / latin1 era) keeps
-- its old charset. Run this file by hand against each such database
-- (e.g. `agrumyapi` on invent.hr).
--
-- SAFE TO RE-RUN: CONVERT TO CHARACTER SET is idempotent - converting a table that is
-- already utf8mb4_unicode_ci is a no-op.
--
-- BEFORE RUNNING: take a backup. CONVERT TO CHARACTER SET rewrites every row and can
-- widen column storage; on a large `sensorData` table it may take a while and hold a
-- metadata lock.
--
-- Collation: utf8mb4_unicode_ci is chosen for broad MySQL/MariaDB compatibility rather
-- than a server-specific default (utf8mb4_0900_ai_ci / utf8mb4_uca1400_ai_ci).
--
-- Pre-check (list anything not already utf8mb4):
--   SELECT TABLE_NAME, TABLE_COLLATION FROM information_schema.TABLES
--   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_COLLATION NOT LIKE 'utf8mb4\_%';

ALTER DATABASE `agrumyapi` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- All 20 tables in AgrumyDbContext. Tables with no string columns are listed too so the
-- table default is consistent and future ALTER ... ADD COLUMN inherits utf8mb4.
ALTER TABLE `tenant`                 CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `user`                   CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `userGroup`              CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `userRole`               CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `userRoleScope`          CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `serverConfig`           CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `device`                 CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `deviceUnit`             CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `deviceUnitZone`         CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `deviceType`             CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `deviceTypeService`      CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `deviceTypeRelay`        CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `deviceTypeSensor`       CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `deviceConfigSensor`     CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `deviceConfigController` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `deviceFirmware`         CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `sensorData`             CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `sensorDataReport`       CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `eventDevice`            CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
ALTER TABLE `eventService`           CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- Sanity check after running (expect zero rows):
--   SELECT TABLE_NAME, TABLE_COLLATION FROM information_schema.TABLES
--   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_COLLATION NOT LIKE 'utf8mb4\_%';
--   SELECT TABLE_NAME, COLUMN_NAME, CHARACTER_SET_NAME FROM information_schema.COLUMNS
--   WHERE TABLE_SCHEMA = DATABASE() AND CHARACTER_SET_NAME IS NOT NULL
--     AND CHARACTER_SET_NAME <> 'utf8mb4';
