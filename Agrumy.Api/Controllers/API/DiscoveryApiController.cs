using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace api.Controllers.API
{
    /// <summary>Roadmap #268 "Scan for new devices" - device-facing report intake only; the scan
    /// trigger and result-aggregation endpoints land in later steps, on this same controller.</summary>
    [Route("/api/Discovery")]
    public class DiscoveryApiController(IRepository repo, ICache cache) : ApiControllerBase(repo, cache)
    {
        /// <summary>No identity field in the body by design - the scanning device comes exclusively
        /// from the authenticated apiId, same rule as DeviceApiController.PushEvent.</summary>
        [HttpPost("Report")]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.SessionPolicy)]
        public async Task<ActionResult> Report([FromBody] DiscoveryReportRequest value)
        {
            if (string.IsNullOrWhiteSpace(value.DiscoveredApMac))
            {
                return BadRequest("discoveredApMac is required.");
            }

            string apiId = HttpContext.DeviceApiId()!;
            Device? device = await Repo.DeviceGetByApiIdAsync(apiId);
            if (device is null)
            {
                return Unauthorized();
            }

            await Repo.DiscoveryReportAddAsync(device.IDDevice!.Value, value.DiscoveredApMac, value.Rssi);
            return Ok();
        }
    }
}
