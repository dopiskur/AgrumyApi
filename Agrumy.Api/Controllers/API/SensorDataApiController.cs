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
            await Repo.SensorDataPushAsync(jsonArray);
            return Ok(Cache.GetDeviceCache(HttpContext.DeviceApiId()!).ConfigVersion);
        }

        [HttpDelete]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult> Delete(int deviceID, int timeMDMY = 0, int timeRange = 0)
        {
            await Repo.SensorDataDeleteAsync(CallerTenantId, deviceID, timeRange, timeMDMY);
            return Ok();
        }

        [HttpGet("Report")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<SensorDataReport>>> ReportGet(int? getData, int? idDevice, int? iDSensorDataReport) =>
            Ok(await Repo.SensorDataReportGetAsync(CallerTenantId, getData, idDevice, iDSensorDataReport));
    }
}
