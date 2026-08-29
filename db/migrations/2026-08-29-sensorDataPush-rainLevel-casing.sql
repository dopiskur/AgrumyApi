-- Style-only fix: the SensorDataPush procedure's JSON_TABLE alias / INSERT column
-- list used `rainlevel` (all lowercase) while every other column in the same block
-- is camelCase (`soilTemperature`, `waterLevel`, `liquidPH`). MySQL identifiers are
-- not case-sensitive so this changed nothing functionally, but the inconsistency
-- read like a bug (and produced a false "known mismatch" note in
-- contracts/device-api/sensordata.request.schema.json, now removed).
--
-- The JSON path `'$.rainLevel'` (capital L) was already correct and matches the
-- firmware key - it is NOT changed here.
--
-- WHY MANUAL: EnsureSchemaAsync() returns early once the `device` table exists, so
-- it will not re-apply CREATE OR REPLACE PROCEDURE to an existing database. Run this
-- by hand against each live database (e.g. agrumyapi on invent.hr).
--
-- SAFE TO RE-RUN: CREATE OR REPLACE PROCEDURE is idempotent.
--
-- Optional pre-check - if the live proc already reads `rainLevel` everywhere (as the
-- repo does), this migration is a no-op beyond re-defining an identical proc:
--   SHOW CREATE PROCEDURE SensorDataPush;

DELIMITER $$

CREATE OR REPLACE PROCEDURE `SensorDataPush`(
	jsonData LONGTEXT
)
BEGIN
INSERT
	INTO sensorData (
	deviceID,
	tenantID,
	deviceUnitID,
    deviceUnitZoneID,
    battery,
    temperature,
    soilTemperature,
    humidity,
    moisture,
    light,
    co2,
    tvoc,
    barometer,
    liquidPH,
    rainLevel,
    waterLevel,
    wind,
    dateCreated
)
  SELECT
	j.deviceID,
	j.tenantID,
    j.deviceUnitID,
    j.deviceUnitZoneID,
    j.battery,
    j.temperature,
    j.soilTemperature,
    j.humidity,
    j.moisture,
    j.light,
    j.co2,
    j.tvoc,
    j.barometer,
    j.liquidPH,
    j.rainLevel,
    j.waterLevel,
    j.wind,
    j.dateCreated
  FROM JSON_TABLE(
    jsonData, '$[*]' COLUMNS(
	  deviceID INT PATH '$.deviceID',
	  tenantID INT PATH '$.tenantID',
	  deviceUnitID INT PATH '$.deviceUnitID',
	  deviceUnitZoneID INT PATH '$.deviceUnitZoneID',
      battery TINYINT PATH '$.battery',
      temperature DOUBLE PATH '$.temperature',
      soilTemperature DOUBLE PATH '$.soilTemperature',
      humidity DOUBLE PATH '$.humidity',
      moisture INT PATH '$.moisture',
      light TINYINT PATH '$.light',
      co2 INT PATH '$.co2',
      tvoc INT PATH '$.tvoc',
      barometer DOUBLE PATH '$.barometer',
      liquidPH DOUBLE PATH '$.liquidPH',
      rainLevel INT PATH '$.rainLevel',
      waterLevel TINYINT PATH '$.waterLevel',
      wind SMALLINT PATH '$.wind',
      dateCreated DATETIME PATH '$.dateCreated'
    )
  ) AS j;

  -- INSERT INTO EVENTDEVICE
END $$

DELIMITER ;
