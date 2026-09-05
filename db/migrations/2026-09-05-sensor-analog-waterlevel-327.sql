-- Firmware's sensor_analog_waterLevel() (deviceTypeSensor 2003) has been a real, working sensor
-- since it was written, but this row was never added to any deviceTypeSensor seed - a device
-- owner could never actually select it for SensorWaterLevel via the Web dropdown on any install.
--
-- WHY THIS IS MANUAL: see 2026-08-31-deviceDiagnostic-table.sql.
-- SAFE TO RE-RUN: the INSERT uses WHERE NOT EXISTS.

INSERT INTO `deviceTypeSensor` (`IDDeviceTypeSensor`, `SensorName`, `SensorDescription`, `Battery`,
                                 `Temperature`, `TemperatureSoil`, `Humidity`, `Moisture`, `Light`,
                                 `Co2`, `Tvoc`, `Barometer`, `WaterPH`, `WaterTankLevel`, `RainLevel`, `Wind`)
SELECT 2003, 'Analog water tank', NULL, 0,
       0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0
WHERE NOT EXISTS (SELECT 1 FROM `deviceTypeSensor` WHERE `IDDeviceTypeSensor` = 2003);

-- Sanity check after running:
--   SELECT * FROM `deviceTypeSensor` WHERE `IDDeviceTypeSensor` = 2003;
