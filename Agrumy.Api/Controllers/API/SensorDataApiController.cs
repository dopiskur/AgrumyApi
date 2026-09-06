using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json.Nodes;

namespace api.Controllers.API
{
    [Route("/api/SensorData")]
    public class SensorDataController(ISensorDataRepository sensorDataRepo, IDeviceRepository deviceRepo, IUserRepository userRepo, IAuditLogRepository auditLogRepo, ICache cache) : ApiControllerBase(userRepo, auditLogRepo, cache)
    {
        // Bounds SensorDataGetAsync's unbounded ToListAsync() and keeps DateTime.AddXxx clear of overflow.
        private static bool IsWithinMaxTimeRange(int? timeMDMY, int timeRange) => timeMDMY switch
        {
            0 => timeRange <= 527040, // minutes, ~1 year
            1 => timeRange <= 3660,   // days, ~10 years
            2 => timeRange <= 120,    // months, ~10 years
            3 => timeRange <= 10,     // years
            _ => true,                // an invalid timeMDMY is handled downstream by SensorDataGetAsync's own check
        };

        [HttpGet]
        [Authorize]
        public async Task<ActionResult<string>> Get(int? deviceID, int? timeRange = 60, int? timeMDMY = 0, int? buildReport = 0)
        {
            if (timeRange is int range && !IsWithinMaxTimeRange(timeMDMY, range))
            {
                return BadRequest($"timeRange {range} exceeds the maximum allowed for this unit.");
            }

            // Stays tenant-scoped even for a Global reader, unlike DevicesGet/DeviceFleetGet.
            return Ok(await sensorDataRepo.SensorDataGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId, deviceID, timeRange, timeMDMY, buildReport));
        }

        // A real device batch is ~20-30 readings (RAM spills at 8192 bytes) - generous headroom.
        private const int MaxSensorDataBatchSize = 1000;

        [HttpPost]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.SessionPolicy)]
        public async Task<ActionResult<int?>> Post([FromBody] JsonArray jsonArray)
        {
            if (jsonArray.Count > MaxSensorDataBatchSize)
            {
                return BadRequest($"Batch too large: {jsonArray.Count} readings, max {MaxSensorDataBatchSize} per request.");
            }

            string apiId = HttpContext.DeviceApiId()!;

            // Device/tenant come from the authenticated identity, never from the payload.
            Device? device = await deviceRepo.DeviceGetByApiIdAsync(apiId);
            if (device is null)
            {
                return Unauthorized();
            }

            await sensorDataRepo.SensorDataPushAsync(jsonArray, device.IDDevice!.Value, device.TenantID,
                device.DeviceFarmUnitID, device.DeviceFarmUnitZoneID);

            return Ok(device.ConfigVersion);
        }

        /// Deleting telemetry is device management, gated to the device-manager roles; the target device's own tenant is resolved and checked explicitly.
        [HttpDelete]
        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult> Delete(int deviceID, int timeMDMY = 0, int timeRange = 0)
        {
            Device? device = await deviceRepo.DeviceGetByIdAsync(deviceID);
            if (device is null)
            {
                return NotFound();
            }
            if (!CallerManagesDevices(device.TenantID))
            {
                return StatusCode(403, "Device belongs to a different tenant");
            }

            await sensorDataRepo.SensorDataDeleteAsync(device.TenantID, deviceID, timeRange, timeMDMY);
            return Ok();
        }

        [HttpGet("Report")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<SensorDataReport>>> ReportGet(int? getData, int? idDevice, int? iDSensorDataReport) =>
            // Same tenant-scoping as Get above.
            Ok(await sensorDataRepo.SensorDataReportGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId, getData, idDevice, iDSensorDataReport));

        [HttpGet("ZoneAverage")]
        [Authorize]
        public async Task<ActionResult<string>> ZoneAverageGet(int deviceFarmUnitZoneID, int? timeRange = 60, int? timeMDMY = 0)
        {
            if (timeRange is int range && !IsWithinMaxTimeRange(timeMDMY, range))
            {
                return BadRequest($"timeRange {range} exceeds the maximum allowed for this unit.");
            }
            return Ok(await sensorDataRepo.SensorDataZoneAverageGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId, deviceFarmUnitZoneID, timeRange, timeMDMY));
        }

        [HttpGet("UnitAverage")]
        [Authorize]
        public async Task<ActionResult<string>> UnitAverageGet(int deviceFarmUnitID, int? timeRange = 60, int? timeMDMY = 0)
        {
            if (timeRange is int range && !IsWithinMaxTimeRange(timeMDMY, range))
            {
                return BadRequest($"timeRange {range} exceeds the maximum allowed for this unit.");
            }
            return Ok(await sensorDataRepo.SensorDataUnitAverageGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId, deviceFarmUnitID, timeRange, timeMDMY));
        }
    }
}
