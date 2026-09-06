using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace api.Controllers.API
{
    /// Real-time relay on/off state, parallel to SensorDataController but fired on every actual relay CHANGE rather than a fixed interval - see api.Dal.Entities.ControllerDataRow.
    [Route("/api/ControllerData")]
    public class ControllerDataApiController(IRepository repo, ICache cache) : ApiControllerBase(repo, cache)
    {
        private const int MaxBatchSize = 32; // one entry per RelayFunction at most - generous headroom over RelaySlotLimits.MaxSlots.

        [HttpPost]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.SessionPolicy)]
        public async Task<ActionResult> Post([FromBody] IList<ControllerDataPush> entries)
        {
            if (entries.Count > MaxBatchSize)
            {
                return BadRequest($"Batch too large: {entries.Count} entries, max {MaxBatchSize} per request.");
            }

            string apiId = HttpContext.DeviceApiId()!;
            Device? device = await Repo.DeviceGetByApiIdAsync(apiId);
            if (device is null)
            {
                return Unauthorized();
            }

            await Repo.ControllerDataPushAsync(device.IDDevice!.Value, device.TenantID, entries);
            return Ok();
        }

        [Authorize]
        [HttpGet]
        public async Task<ActionResult<IList<ControllerDataStatus>>> Get(int idDevice)
        {
            Device? device = await Repo.DeviceGetByIdAsync(idDevice);
            if (device is null)
            {
                return NotFound();
            }
            if (!CallerReadsDevicesGlobally && device.TenantID != CallerTenantId)
            {
                return StatusCode(403, "Device belongs to a different tenant");
            }

            return Ok(await Repo.ControllerDataGetAsync(idDevice));
        }
    }
}
