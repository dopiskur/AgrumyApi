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
    public class SensorDataController(IRepository repo, ICache cache) : ApiControllerBase(repo, cache)
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
            return Ok(await Repo.SensorDataGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId, deviceID, timeRange, timeMDMY, buildReport));
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
            Device? device = await Repo.DeviceGetByApiIdAsync(apiId);
            if (device is null)
            {
                return Unauthorized();
            }

            await Repo.SensorDataPushAsync(jsonArray, device.IDDevice!.Value, device.TenantID,
                device.DeviceUnitID, device.DeviceUnitZoneID);

            return Ok(device.ConfigVersion);
        }

        /// Deleting telemetry is device management, gated to the device-manager roles; the target device's own tenant is resolved and checked explicitly.
        [HttpDelete]
        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult> Delete(int deviceID, int timeMDMY = 0, int timeRange = 0)
        {
            Device? device = await Repo.DeviceGetByIdAsync(deviceID);
            if (device is null)
            {
                return NotFound();
            }
            if (!CallerManagesDevices(device.TenantID))
            {
                return StatusCode(403, "Device belongs to a different tenant");
            }

            await Repo.SensorDataDeleteAsync(device.TenantID, deviceID, timeRange, timeMDMY);
            return Ok();
        }

        [HttpGet("Report")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<SensorDataReport>>> ReportGet(int? getData, int? idDevice, int? iDSensorDataReport) =>
            // Same tenant-scoping as Get above.
            Ok(await Repo.SensorDataReportGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId, getData, idDevice, iDSensorDataReport));

        [HttpGet("ZoneAverage")]
        [Authorize]
        public async Task<ActionResult<string>> ZoneAverageGet(int deviceUnitZoneID, int? timeRange = 60, int? timeMDMY = 0)
        {
            if (timeRange is int range && !IsWithinMaxTimeRange(timeMDMY, range))
            {
                return BadRequest($"timeRange {range} exceeds the maximum allowed for this unit.");
            }
            return Ok(await Repo.SensorDataZoneAverageGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId, deviceUnitZoneID, timeRange, timeMDMY));
        }

        [HttpGet("UnitAverage")]
        [Authorize]
        public async Task<ActionResult<string>> UnitAverageGet(int deviceUnitID, int? timeRange = 60, int? timeMDMY = 0)
        {
            if (timeRange is int range && !IsWithinMaxTimeRange(timeMDMY, range))
            {
                return BadRequest($"timeRange {range} exceeds the maximum allowed for this unit.");
            }
            return Ok(await Repo.SensorDataUnitAverageGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId, deviceUnitID, timeRange, timeMDMY));
        }
    }
}
