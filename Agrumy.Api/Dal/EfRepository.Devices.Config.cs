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

            var row = await db.DeviceConfigControllers
                .FirstOrDefaultAsync(c => c.IDDeviceConfigController == cfg.IDDeviceConfigController);
            if (row != null)
            {
                // The proc declared these params as int (columns are double) so historically the
                // values were truncated. Phase 1 stores the real double instead - a deliberate,
                // documented deviation from the proc.
                row.TempLow = cfg.TempLow;
                row.TempHigh = cfg.TempHigh;
                row.HumidLow = cfg.HumidLow;
                row.HumidHigh = cfg.HumidHigh;
                row.MoistLow = cfg.MoistLow;
                row.MoistHigh = cfg.MoistHigh;
                row.LightLow = cfg.LightLow;
                row.LightHigh = cfg.LightHigh;
                row.WaterLow = cfg.WaterLow;
                row.WaterHigh = cfg.WaterHigh;
                row.WaterLevelHysteresis = cfg.WaterLevelHysteresis;
                row.TemperatureHysteresis = cfg.TemperatureHysteresis;
                row.HumidityHysteresis = cfg.HumidityHysteresis;
                row.LightHysteresis = cfg.LightHysteresis;
                row.VentilationIntervalEnabled = cfg.VentilationIntervalEnabled;
                row.VentilationInterval = cfg.VentilationInterval;
                row.VentilationIntervalLength = cfg.VentilationIntervalLength;
                row.LightIntervalEnabled = cfg.LightIntervalEnabled;
                row.LightInterval = cfg.LightInterval;
                row.LightIntervalLength = cfg.LightIntervalLength;
                row.HeatingIntervalEnabled = cfg.HeatingIntervalEnabled;
                row.HeatingInterval = cfg.HeatingInterval;
                row.HeatingIntervalLength = cfg.HeatingIntervalLength;
                row.WaterPumpIntervalEnabled = cfg.WaterPumpIntervalEnabled;
                row.WaterPumpInterval = cfg.WaterPumpInterval;
                row.WaterPumpIntervalLength = cfg.WaterPumpIntervalLength;
                row.VentilationScheduleEnabled = cfg.VentilationScheduleEnabled;
                row.VentilationScheduleDaysOfWeek = cfg.VentilationScheduleDaysOfWeek;
                row.VentilationScheduleStart = cfg.VentilationScheduleStart;
                row.VentilationScheduleDuration = cfg.VentilationScheduleDuration;
                row.LightScheduleEnabled = cfg.LightScheduleEnabled;
                row.LightScheduleDaysOfWeek = cfg.LightScheduleDaysOfWeek;
                row.LightScheduleStart = cfg.LightScheduleStart;
                row.LightScheduleDuration = cfg.LightScheduleDuration;
                row.HeatingScheduleEnabled = cfg.HeatingScheduleEnabled;
                row.HeatingScheduleDaysOfWeek = cfg.HeatingScheduleDaysOfWeek;
                row.HeatingScheduleStart = cfg.HeatingScheduleStart;
                row.HeatingScheduleDuration = cfg.HeatingScheduleDuration;
                row.WaterPumpScheduleEnabled = cfg.WaterPumpScheduleEnabled;
                row.WaterPumpScheduleDaysOfWeek = cfg.WaterPumpScheduleDaysOfWeek;
                row.WaterPumpScheduleStart = cfg.WaterPumpScheduleStart;
                row.WaterPumpScheduleDuration = cfg.WaterPumpScheduleDuration;
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

            var row = await db.DeviceConfigSensors
                .FirstOrDefaultAsync(c => c.IDDeviceConfigSensor == cfg.IDDeviceConfigSensor);
            if (row != null)
            {
                row.SensorBattery = cfg.SensorBattery;
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

        private static DeviceConfigController ToDto(DeviceConfigControllerRow c) => new()
        {
            IDDeviceConfigController = c.IDDeviceConfigController,
            TempLow = c.TempLow,
            TempHigh = c.TempHigh,
            HumidLow = c.HumidLow,
            HumidHigh = c.HumidHigh,
            MoistLow = c.MoistLow,
            MoistHigh = c.MoistHigh,
            LightLow = c.LightLow,
            LightHigh = c.LightHigh,
            WaterLow = c.WaterLow,
            WaterHigh = c.WaterHigh,
            WaterLevelHysteresis = c.WaterLevelHysteresis,
            TemperatureHysteresis = c.TemperatureHysteresis,
            HumidityHysteresis = c.HumidityHysteresis,
            LightHysteresis = c.LightHysteresis,
            VentilationIntervalEnabled = c.VentilationIntervalEnabled,
            VentilationInterval = c.VentilationInterval,
            VentilationIntervalLength = c.VentilationIntervalLength,
            LightIntervalEnabled = c.LightIntervalEnabled,
            LightInterval = c.LightInterval,
            LightIntervalLength = c.LightIntervalLength,
            HeatingIntervalEnabled = c.HeatingIntervalEnabled,
            HeatingInterval = c.HeatingInterval,
            HeatingIntervalLength = c.HeatingIntervalLength,
            WaterPumpIntervalEnabled = c.WaterPumpIntervalEnabled,
            WaterPumpInterval = c.WaterPumpInterval,
            WaterPumpIntervalLength = c.WaterPumpIntervalLength,
            VentilationScheduleEnabled = c.VentilationScheduleEnabled,
            VentilationScheduleDaysOfWeek = c.VentilationScheduleDaysOfWeek,
            VentilationScheduleStart = c.VentilationScheduleStart,
            VentilationScheduleDuration = c.VentilationScheduleDuration,
            LightScheduleEnabled = c.LightScheduleEnabled,
            LightScheduleDaysOfWeek = c.LightScheduleDaysOfWeek,
            LightScheduleStart = c.LightScheduleStart,
            LightScheduleDuration = c.LightScheduleDuration,
            HeatingScheduleEnabled = c.HeatingScheduleEnabled,
            HeatingScheduleDaysOfWeek = c.HeatingScheduleDaysOfWeek,
            HeatingScheduleStart = c.HeatingScheduleStart,
            HeatingScheduleDuration = c.HeatingScheduleDuration,
            WaterPumpScheduleEnabled = c.WaterPumpScheduleEnabled,
            WaterPumpScheduleDaysOfWeek = c.WaterPumpScheduleDaysOfWeek,
            WaterPumpScheduleStart = c.WaterPumpScheduleStart,
            WaterPumpScheduleDuration = c.WaterPumpScheduleDuration,
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
