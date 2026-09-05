-- Roadmap #308: "Relay" collided between the electrical relay outputs (Relay1-8,
-- deviceTypeRelay, deviceConfigController.RelayEnabled - all UNTOUCHED here) and the separate
-- LoRa/HTTP proxy device concept (Agrumy.Relay project, device.IsRelay/RelayProfile,
-- serverConfig.RelayEnabled/RelayMode/RelayWaitWindowSeconds, relayDeviceMapping table).
-- Renames only the proxy-device side to "Gateway" to match the renamed
-- Agrumy.Gateway project/C# identifiers - confirmed via SHOW CREATE TABLE before writing this.
--
-- WHY THIS IS MANUAL: see 2026-08-30-user-activation-columns.sql - EnsureSchemaAsync() only
-- provisions a brand-new (zero-table) database, never alters an existing table.
--
-- NOT SAFE TO RE-RUN: a second run fails loudly (old column/table/index names no longer exist)
-- rather than doing anything harmful - there is nothing to guard, a rename is inherently a
-- one-shot operation. Same rationale as 2026-09-02-devicecontroller-intervallength-rename.sql.

ALTER TABLE `device`
  CHANGE COLUMN `IsRelay` `IsGateway` tinyint(1) NOT NULL DEFAULT 0,
  CHANGE COLUMN `RelayProfile` `GatewayProfile` int(11) DEFAULT NULL;

ALTER TABLE `serverConfig`
  CHANGE COLUMN `RelayEnabled` `GatewayEnabled` tinyint(1) NOT NULL DEFAULT 0,
  CHANGE COLUMN `RelayMode` `GatewayMode` int(11) NOT NULL DEFAULT 0,
  CHANGE COLUMN `RelayWaitWindowSeconds` `GatewayWaitWindowSeconds` int(11) NOT NULL DEFAULT 30;

-- FK/index names can't be renamed while the FK still references the old column name, so drop
-- and recreate rather than RENAME INDEX first.
ALTER TABLE `relayDeviceMapping`
  DROP FOREIGN KEY `fk_relayDeviceMapping_relay`,
  DROP FOREIGN KEY `fk_relayDeviceMapping_device`;

ALTER TABLE `relayDeviceMapping`
  CHANGE COLUMN `IDRelayDeviceMapping` `IDGatewayDeviceMapping` int(11) NOT NULL AUTO_INCREMENT,
  CHANGE COLUMN `IDRelayDevice` `IDGatewayDevice` int(11) NOT NULL;

ALTER TABLE `relayDeviceMapping`
  RENAME INDEX `ux_relayDeviceMapping_relay_deveui` TO `ux_gatewayDeviceMapping_gateway_deveui`,
  RENAME INDEX `ix_relayDeviceMapping_IDDevice` TO `ix_gatewayDeviceMapping_IDDevice`;

ALTER TABLE `relayDeviceMapping`
  ADD CONSTRAINT `fk_gatewayDeviceMapping_gateway` FOREIGN KEY (`IDGatewayDevice`) REFERENCES `device` (`IDDevice`),
  ADD CONSTRAINT `fk_gatewayDeviceMapping_device` FOREIGN KEY (`IDDevice`) REFERENCES `device` (`IDDevice`);

RENAME TABLE `relayDeviceMapping` TO `gatewayDeviceMapping`;

-- Sanity check after running:
--   SHOW COLUMNS FROM `device` LIKE 'IsGateway'; SHOW COLUMNS FROM `device` LIKE 'GatewayProfile';
--   SHOW COLUMNS FROM `serverConfig` LIKE 'Gateway%';
--   SHOW CREATE TABLE `gatewayDeviceMapping`;
--   SHOW COLUMNS FROM `device` LIKE 'IsRelay';  -- expect 0 rows
--   SHOW COLUMNS FROM `serverConfig` LIKE 'Relay%';  -- expect 0 rows
