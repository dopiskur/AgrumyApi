-- JWT refresh tokens: new `userRefreshToken` table.
--
-- WHY THIS IS MANUAL:
-- EnsureSchemaAsync() (Agrumy.Api's startup schema check) calls EnsureCreatedAsync(), which only
-- provisions a database that has ZERO tables - it never adds a table to a database that already
-- has others (which invent.hr always does, and any already-provisioned dev/test database too).
-- Run this by hand against each such database before deploying the refresh-token code.
--
-- SAFE TO RE-RUN: guarded with IF NOT EXISTS.
--
-- Only the SHA-256 hash of each refresh token is ever stored (TokenHash, hex-encoded = 64 chars),
-- never the plaintext - see UserApiController.HashRefreshToken.

CREATE TABLE IF NOT EXISTS `userRefreshToken` (
  `IDRefreshToken` INT NOT NULL AUTO_INCREMENT,
  `UserID` INT NOT NULL,
  `TokenHash` VARCHAR(64) NOT NULL,
  `ExpiresAt` DATETIME(6) NOT NULL,
  `CreatedAt` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `RevokedAt` DATETIME(6) DEFAULT NULL,
  `ReplacedByTokenHash` VARCHAR(64) DEFAULT NULL,
  PRIMARY KEY (`IDRefreshToken`),
  UNIQUE KEY `TokenHash_UNIQUE` (`TokenHash`),
  KEY `ix_userRefreshToken_userID` (`UserID`),
  CONSTRAINT `FK_userRefreshToken_user_UserID` FOREIGN KEY (`UserID`) REFERENCES `user` (`IDUser`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Sanity check after running:
--   SHOW CREATE TABLE `userRefreshToken`;
