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
        // Same ~10-years-of-history ceiling regardless of unit - generous for any real dashboard use,
        // but bounds SensorDataGetAsync's unbounded ToListAsync() and keeps the DateTime.AddXxx call
        // there well clear of its MinValue/MaxValue range (an extreme timeRange used to throw an
        // uncaught ArgumentOutOfRangeException there instead of failing cleanly here).
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

            // Was always tenant-scoped even for a Global reader, unlike DevicesGet/DeviceFleetGet.
            return Ok(await Repo.SensorDataGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId, deviceID, timeRange, timeMDMY, buildReport));
        }

        // A real device batch (RAM spills at SENSOR_BUFFER_SPILL_BYTES=8192, ~416 bytes/record) is
        // on the order of 20-30 readings - generous headroom over that, but still bounds the
        // RAM/EF change-tracker cost a compromised or buggy device could otherwise inflict by
        // sending an arbitrarily large array.
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

        /// <summary>Deleting telemetry is device management, gated to the device-manager roles; the target device's own tenant is resolved and checked explicitly.</summary>
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
            // Same fix as Get above.
            Ok(await Repo.SensorDataReportGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId, getData, idDevice, iDSensorDataReport));
    }
}
