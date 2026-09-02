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
            if (row == null)
            {
                return null;
            }
            var slots = await db.DeviceScheduleSlots.AsNoTracking()
                .Where(s => s.DeviceConfigControllerID == deviceConfigControllerID)
                .ToListAsync();
            return ToDto(row, slots);
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
                row.RelayEnabled = cfg.RelayEnabled;
                row.Relay1 = cfg.Relay1;
                row.Relay2 = cfg.Relay2;
                row.Relay3 = cfg.Relay3;
                row.Relay4 = cfg.Relay4;
                row.Relay5 = cfg.Relay5;
                row.Relay6 = cfg.Relay6;
                row.Relay7 = cfg.Relay7;
                row.Relay8 = cfg.Relay8;

                // Roadmap #115: delete-all-then-reinsert - AgrumyDbContext does not configure EF
                // relationships for LINQ joins (see its class comment), so this is the simplest
                // correct way to replace a controller's full slot set on every save. Immediate
                // (not queued on the change tracker), so it runs before the AddRange below.
                await db.DeviceScheduleSlots
                    .Where(s => s.DeviceConfigControllerID == row.IDDeviceConfigController)
                    .ExecuteDeleteAsync();
                db.DeviceScheduleSlots.AddRange(
                    BuildScheduleSlotRows(row.IDDeviceConfigController, 1, cfg.VentilationSchedule)
                    .Concat(BuildScheduleSlotRows(row.IDDeviceConfigController, 2, cfg.LightSchedule))
                    .Concat(BuildScheduleSlotRows(row.IDDeviceConfigController, 3, cfg.HeatingSchedule))
                    .Concat(BuildScheduleSlotRows(row.IDDeviceConfigController, 4, cfg.WaterPumpSchedule)));
            }

            var deviceRow = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == idDevice);
            if (deviceRow != null)
            {
                deviceRow.ConfigVersion = (deviceRow.ConfigVersion ?? 0) + 1;
            }

            await db.SaveChangesAsync(); // one transaction: config row + ConfigVersion bump
        }

        private static IEnumerable<DeviceScheduleSlotRow> BuildScheduleSlotRows(
            int deviceConfigControllerId, int relayFunction, IEnumerable<DeviceScheduleSlot>? slots) =>
            (slots ?? []).Select(s => new DeviceScheduleSlotRow
            {
                DeviceConfigControllerID = deviceConfigControllerId,
                RelayFunction = relayFunction,
                DaysOfWeek = s.DaysOfWeek,
                Start = s.Start,
                Duration = s.Duration,
            });

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

        private static DeviceConfigController ToDto(DeviceConfigControllerRow c, IList<DeviceScheduleSlotRow> slots) => new()
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
            VentilationSchedule = ScheduleSlotsFor(slots, 1),
            LightSchedule = ScheduleSlotsFor(slots, 2),
            HeatingSchedule = ScheduleSlotsFor(slots, 3),
            WaterPumpSchedule = ScheduleSlotsFor(slots, 4),
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

        private static List<DeviceScheduleSlot> ScheduleSlotsFor(IList<DeviceScheduleSlotRow> slots, int relayFunction) =>
            slots.Where(s => s.RelayFunction == relayFunction)
                 .Select(s => new DeviceScheduleSlot { DaysOfWeek = s.DaysOfWeek, Start = s.Start, Duration = s.Duration })
                 .ToList();
    }
}
