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
        [HttpGet]
        [Authorize]
        public async Task<ActionResult<string>> Get(int? deviceID, int? timeRange = 60, int? timeMDMY = 0, int? buildReport = 0) =>
            Ok(await Repo.SensorDataGetAsync(CallerTenantId, deviceID, timeRange, timeMDMY, buildReport));

        [HttpPost]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.SessionPolicy)]
        public async Task<ActionResult<int?>> Post([FromBody] JsonArray jsonArray)
        {
            string apiId = HttpContext.DeviceApiId()!;

            // Resolve the device/tenant from the authenticated identity and hand them to the repo,
            // which writes them onto every row and ignores whatever the payload claims. An
            // authenticated apiId with no device row must not fall back to trusting the payload.
            Device? device = await Repo.DeviceGetByApiIdAsync(apiId);
            if (device is null)
            {
                return Unauthorized();
            }

            await Repo.SensorDataPushAsync(jsonArray, device.IDDevice!.Value, device.TenantID,
                device.DeviceUnitID, device.DeviceUnitZoneID);

            // Roadmap #106: the device row already in hand is the authoritative ConfigVersion - no
            // reason to route this through the session cache (which no longer carries it at all).
            return Ok(device.ConfigVersion);
        }

        /// <summary>#66 Phase 2: deleting telemetry is device management, so the gate moved from the
        /// binary "admin" to the device-manager roles, and the target device's own tenant is resolved
        /// and checked explicitly (previously a cross-tenant id silently deleted zero rows).</summary>
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
            Ok(await Repo.SensorDataReportGetAsync(CallerTenantId, getData, idDevice, iDSensorDataReport));
    }
}
