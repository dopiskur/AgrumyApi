-- EfRepository.ServerConfigGetAsync/UpdateAsync read/write MqttTransportEnabled/MqttBrokerHost/
-- MqttBrokerPort/MqttUsername/MqttPassword unconditionally, so every ServerConfig query fails with
-- "Unknown column" until this runs.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: ADD COLUMN uses IF NOT EXISTS.

ALTER TABLE `serverConfig`
  ADD COLUMN IF NOT EXISTS `MqttTransportEnabled` TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS `MqttBrokerHost` VARCHAR(255) NULL,
  ADD COLUMN IF NOT EXISTS `MqttBrokerPort` INT NOT NULL DEFAULT 1883,
  ADD COLUMN IF NOT EXISTS `MqttUsername` VARCHAR(255) NULL,
  ADD COLUMN IF NOT EXISTS `MqttPassword` VARCHAR(255) NULL;

-- Sanity check after running:
--   SHOW COLUMNS FROM `serverConfig` LIKE 'Mqtt%';
