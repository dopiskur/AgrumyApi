using api.Dal;
using api.Dal.Interface;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Net.Http.Headers;
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

        // GET: api/<SensorDataController>



        //[HttpGet("SensorData")]
        [HttpGet]
        [Authorize]
        public async Task<ActionResult<SensorData>> Get(int? deviceID, int? timeRange = 60, int? timeMDMY = 0, int? buildReport=0) // 0 minute, 1 days, 2 months, 3 years
        {

            try
            {
                string sensorData = await RepoFactory.GetRepo().SensorDataGetAsync(0, deviceID, timeRange, timeMDMY, buildReport);
                return Ok(sensorData);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "SensorData Get failed for device {DeviceID}", deviceID);
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }



        //[HttpPost("SensorData")]
        [HttpPost]
        [EnableRateLimiting("device-data")]
        public async Task<ActionResult<string>> Post([FromBody] JsonArray jsonArray)
        {
            try
            {

                if (AuthenticationHeaderValue.TryParse(Request.Headers["apiId"], out var apiId) && AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var authKey))
                {

                    if (!DeviceApiController.GetAuth(apiId.ToString(), authKey.ToString()))
                    {
                        return StatusCode(401);
                    }

                    if (!ModelState.IsValid) { return BadRequest(ModelState); }

                    await RepoFactory.GetRepo().SensorDataPushAsync(jsonArray);

                    DeviceCache? deviceCache = RepoFactory.GetCache().GetDeviceCache(apiId.ToString());
                    return Ok(deviceCache.ConfigVersion);
                }

                return StatusCode(401, "Wrong Id or Key");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "SensorData Post failed");
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }



        //[HttpDelete("{id}")]
        // mozda cu koristit post za ovo, jer ne mogu dobit u body resposne koliko je redova obrisano, delete to ne podrzava
        [HttpDelete]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult> Delete(int deviceID, int tenantID, int timeMDMY = 0, int timeRange = 0)
        {

            try
            {
                await RepoFactory.GetRepo().SensorDataDeleteAsync(deviceID, tenantID, timeMDMY, timeRange);

                return Ok();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "SensorData Delete failed for device {DeviceID}", deviceID);
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }




        [HttpGet("Report")] // Request authentication
        [Authorize]
        public async Task<ActionResult<IEnumerable<SensorData>>> ReportGet(int? getData, int? idDevice, int? iDSensorDataReport)
        {

            IEnumerable<SensorDataReport>? sensorDataResult = await RepoFactory.GetRepo().SensorDataReportGetAsync(getData, idDevice, iDSensorDataReport);


            return Ok(sensorDataResult);
        }// private su metode koje se koriste interno, ali i dalaje mora imat httpget


    }
}
