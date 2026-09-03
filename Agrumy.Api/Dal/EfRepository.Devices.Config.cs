using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IDeviceRepository members (roadmap #95 split, continuing #74): per-device sensor
    /// and controller config reads/writes.</summary>
    internal partial class EfRepository
    {
        public async Task<DeviceConfigSensor?> DeviceConfigSensorGetAsync(int? deviceConfigSensorID)
        {
            var row = await db.DeviceConfigSensors.AsNoTracking()
                .FirstOrDefaultAsync(c => c.IDDeviceConfigSensor == deviceConfigSensorID);
            return row == null ? null : ToDto(row);
        }

        public async Task<DeviceConfigController?> DeviceConfigControllerGetAsync(int? deviceConfigControllerID)
        {
            var row = await db.DeviceConfigControllers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.IDDeviceConfigController == deviceConfigControllerID);
            return row == null ? null : ToDto(row);
        }

        public async Task<Device?> DeviceGetByDeviceConfigSensorIdAsync(int? deviceConfigSensorID)
        {
            var row = await db.Devices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceConfigSensorID == deviceConfigSensorID);
            return row == null ? null : ToDto(row);
        }

        public async Task<Device?> DeviceGetByDeviceConfigControllerIdAsync(int? deviceConfigControllerID)
        {
            var row = await db.Devices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeviceConfigControllerID == deviceConfigControllerID);
            return row == null ? null : ToDto(row);
        }

        public async Task DeviceConfigControllerUpdateAsync(int? idDevice, DeviceConfigController? cfg)
        {
            if (cfg == null)
            {
                return;
            }

            // Roadmap #78: resolve the target row from idDevice's OWN DeviceConfigControllerID, not
            // cfg.IDDeviceConfigController - the caller's ownership was only checked against idDevice
            // (DeviceApiController.DeviceConfigControllerUpdate), so trusting a client-supplied config
            // id here would let a tampered id overwrite any other device's (even another tenant's)
            // controller config.
            int? ownConfigControllerId = await db.Devices.AsNoTracking()
                .Where(d => d.IDDevice == idDevice)
                .Select(d => d.DeviceConfigControllerID)
                .FirstOrDefaultAsync();

            var row = await db.DeviceConfigControllers
                .FirstOrDefaultAsync(c => c.IDDeviceConfigController == ownConfigControllerId);
            if (row != null)
            {
                // Roadmap #21: only the relay-pin mapping is left here - threshold/hysteresis/
                // interval/schedule/#36-safety-limits all moved to the device's assigned
                // DeviceUnitZone (EfRepository.DeviceUnits.cs's DeviceUnitZoneRule* members), edited
                // from the Zone page now, not this per-device form.
                row.RelayEnabled = cfg.RelayEnabled;
                row.Relay1 = cfg.Relay1;
                row.Relay2 = cfg.Relay2;
                row.Relay3 = cfg.Relay3;
                row.Relay4 = cfg.Relay4;
                row.Relay5 = cfg.Relay5;
                row.Relay6 = cfg.Relay6;
                row.Relay7 = cfg.Relay7;
                row.Relay8 = cfg.Relay8;
            }

            var deviceRow = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == idDevice);
            if (deviceRow != null)
            {
                deviceRow.ConfigVersion = (deviceRow.ConfigVersion ?? 0) + 1;
            }

            await db.SaveChangesAsync(); // one transaction: config row + ConfigVersion bump
        }

        public async Task DeviceConfigSensorUpdateAsync(int? idDevice, DeviceConfigSensor? cfg)
        {
            if (cfg == null)
            {
                return;
            }

            // Roadmap #78: same fix as DeviceConfigControllerUpdateAsync above - resolve the row from
            // idDevice's own DeviceConfigSensorID rather than trusting cfg.IDDeviceConfigSensor.
            int? ownConfigSensorId = await db.Devices.AsNoTracking()
                .Where(d => d.IDDevice == idDevice)
                .Select(d => d.DeviceConfigSensorID)
                .FirstOrDefaultAsync();

            var row = await db.DeviceConfigSensors
                .FirstOrDefaultAsync(c => c.IDDeviceConfigSensor == ownConfigSensorId);
            if (row != null)
            {
                row.SensorBattery = cfg.SensorBattery;
                row.BatteryDividerR1 = cfg.BatteryDividerR1;
                row.BatteryDividerR2 = cfg.BatteryDividerR2;
                row.SensorTemp = cfg.SensorTemp;
                row.SensorTempSoil = cfg.SensorTempSoil;
                row.SensorHumid = cfg.SensorHumid;
                row.SensorMoist = cfg.SensorMoist;
                row.SensorLight = cfg.SensorLight;
                row.SensorCo2 = cfg.SensorCo2;
                row.SensorTvoc = cfg.SensorTvoc;
                row.SensorBarometer = cfg.SensorBarometer;
                row.SensorPH = cfg.SensorPH;
                row.SensorRainLevel = cfg.SensorRainLevel;
                row.SensorWaterLevel = cfg.SensorWaterLevel;
                row.SensorWind = cfg.SensorWind;
            }

            var deviceRow = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == idDevice);
            if (deviceRow != null)
            {
                deviceRow.ConfigVersion = (deviceRow.ConfigVersion ?? 0) + 1;
            }

            await db.SaveChangesAsync(); // one transaction: config row + ConfigVersion bump
        }

        private static DeviceConfigSensor ToDto(DeviceConfigSensorRow c) => new()
        {
            IDDeviceConfigSensor = c.IDDeviceConfigSensor,
            SensorBattery = c.SensorBattery,
            BatteryDividerR1 = c.BatteryDividerR1,
            BatteryDividerR2 = c.BatteryDividerR2,
            SensorTemp = c.SensorTemp,
            SensorTempSoil = c.SensorTempSoil,
            SensorHumid = c.SensorHumid,
            SensorMoist = c.SensorMoist,
            SensorLight = c.SensorLight,
            SensorCo2 = c.SensorCo2,
            SensorTvoc = c.SensorTvoc,
            SensorBarometer = c.SensorBarometer,
            SensorPH = c.SensorPH,
            SensorRainLevel = c.SensorRainLevel,
            SensorWaterLevel = c.SensorWaterLevel,
            SensorWind = c.SensorWind,
        };

        // Roadmap #21: relay-pin mapping only - Rules/WaterPumpMaxRunSeconds/WaterPumpCooldownSeconds
        // on the DTO are populated by DeviceApiController.BuildDeviceConfigAsync from the device's
        // assigned zone, not from this row - see DeviceConfigController's own remarks.
        private static DeviceConfigController ToDto(DeviceConfigControllerRow c) => new()
        {
            IDDeviceConfigController = c.IDDeviceConfigController,
            RelayEnabled = c.RelayEnabled,
            Relay1 = c.Relay1,
            Relay2 = c.Relay2,
            Relay3 = c.Relay3,
            Relay4 = c.Relay4,
            Relay5 = c.Relay5,
            Relay6 = c.Relay6,
            Relay7 = c.Relay7,
            Relay8 = c.Relay8,
        };
    }
}
