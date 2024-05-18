using api.Dal.Interface;
using api.Models;
using api.Security;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using NuGet.Packaging;
using NuGet.Protocol;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Runtime.Caching;
using System.Text.Json.Nodes;


// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace api.Controllers.API
{
    [Route("/api/SensorData")]
    [ApiController]
    public class SensorDataController : ControllerBase
    {
        //public static CacheRepository CacheData = new();
        //public static AuthenticationProvider AuthValidator = new();

        // GET: api/<SensorDataController>



        //[HttpGet("SensorData")]
        [HttpGet]
        [Authorize]
        public ActionResult<SensorData> Get(int? deviceID, int? timeRange = 60, int? timeMDMY = 0, int? buildReport=0) // 0 minute, 1 days, 2 months, 3 years
        {

            try
            {
                string sensorData = RepoFactory.GetRepo().SensorDataGet(0, deviceID, timeRange, timeMDMY, buildReport);
                return Ok(sensorData);
            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }
        }



        //[HttpPost("SensorData")]
        [HttpPost]
        public ActionResult<string> Post([FromBody] JsonArray jsonArray)
        {
            try
            {

                if (AuthenticationHeaderValue.TryParse(Request.Headers["apiId"], out var apiId) && AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var authKey))
                {

                    if (!DeviceApiController.GetAuth(apiId.ToString(), authKey.ToString()))
                    {
                        // DeviceCache? deviceCache2 = RepoFactory.GetCache().GetDeviceCache(apiId.ToString()); // test only
                        return StatusCode(401);
                    }

                    if (!ModelState.IsValid) { return BadRequest(ModelState); }

                    RepoFactory.GetRepo().SensorDataPush(jsonArray);

                    DeviceCache? deviceCache = RepoFactory.GetCache().GetDeviceCache(apiId.ToString());
                    return Ok(deviceCache.ConfigVersion);
                }

                return StatusCode(401, "Wrong Id or Key");
            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }
        }



        //[HttpDelete("{id}")]
        // mozda cu koristit post za ovo, jer ne mogu dobit u body resposne koliko je redova obrisano, delete to ne podrzava
        [HttpDelete]
        //[Authorize]
        public ActionResult Delete(int deviceID, int tenantID, int timeMDMY = 0, int timeRange = 0)
        {

            try
            {
                RepoFactory.GetRepo().SensorDataDelete(deviceID, tenantID, timeMDMY, timeRange);

                return Ok();
            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }
        }




        [HttpGet("Report")] // Request authentication
        [Authorize]
        public ActionResult<IEnumerable<SensorData>> ReportGet(int? getData, int? idDevice, int? iDSensorDataReport)
        {

            IEnumerable<SensorDataReport>? sensorDataResult = RepoFactory.GetRepo().SensorDataReportGet(getData, idDevice, iDSensorDataReport);


            return Ok(sensorDataResult);
        }// private su metode koje se koriste interno, ali i dalaje mora imat httpget


    }
}