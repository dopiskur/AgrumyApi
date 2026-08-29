using api.Dal;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Text.Json.Nodes;


namespace api.Controllers.API
{
    [Route("/api/SensorData")]
    [ApiController]
    public class SensorDataController : ControllerBase
    {
        private readonly ILogger<SensorDataController> _logger;

        public SensorDataController(ILogger<SensorDataController> logger)
        {
            _logger = logger;
        }

        /// <summary>TenantID claim set at login (JwtTokenProvider.CreateToken) - null only if the claim is somehow missing.</summary>
        private int? GetCallerTenantId()
        {
            var identity = HttpContext.User.Identity as ClaimsIdentity;
            var claim = identity?.FindFirst("TenantID");
            return claim != null && int.TryParse(claim.Value, out var tenantId) ? tenantId : null;
        }

        [HttpGet]
        [Authorize]
        public async Task<ActionResult<string>> Get(int? deviceID, int? timeRange = 60, int? timeMDMY = 0, int? buildReport = 0)
        {
            try
            {
                string sensorData = await RepoFactory.GetRepo()
                    .SensorDataGetAsync(GetCallerTenantId(), deviceID, timeRange, timeMDMY, buildReport);
                return Ok(sensorData);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "SensorData Get failed for device {DeviceID}", deviceID);
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }

        [HttpPost]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.SessionPolicy)]
        public async Task<ActionResult<int?>> Post([FromBody] JsonArray jsonArray)
        {
            try
            {
                if (!ModelState.IsValid) { return BadRequest(ModelState); }

                await RepoFactory.GetRepo().SensorDataPushAsync(jsonArray);

                DeviceCache? deviceCache = RepoFactory.GetCache().GetDeviceCache(HttpContext.DeviceApiId()!);
                return Ok(deviceCache.ConfigVersion);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "SensorData Post failed");
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }

        [HttpDelete]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult> Delete(int deviceID, int timeMDMY = 0, int timeRange = 0)
        {
            try
            {
                await RepoFactory.GetRepo().SensorDataDeleteAsync(GetCallerTenantId(), deviceID, timeRange, timeMDMY);
                return Ok();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "SensorData Delete failed for device {DeviceID}", deviceID);
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }

        [HttpGet("Report")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<SensorDataReport>>> ReportGet(int? getData, int? idDevice, int? iDSensorDataReport)
        {
            IEnumerable<SensorDataReport> reports = await RepoFactory.GetRepo()
                .SensorDataReportGetAsync(GetCallerTenantId(), getData, idDevice, iDSensorDataReport);
            return Ok(reports);
        }
    }
}
