using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace api.Controllers.View
{
    public class SensorDataController : Controller
    {



        // GET: SensorDataController
        public ActionResult Index(int? idDevice, int? timeRange=60, int? timeMDMY = 0, int? buildReport=0)
        {

            //HANDLE SESSION
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            if (jwtKey == null || JwtTokenProvider.ValidateToken(jwtKey) == null) { return RedirectToAction("Index", "Login"); }


            // maximum minutes allowed are one day of data
            if (timeRange > 1440)
            {
                timeRange = 1440;
            }

            DeviceView? deviceView = new DeviceView();
            deviceView.Device = RepoFactory.GetApi().DeviceGet(jwtKey, idDevice, null, null).Result;


            var timeRangeMDMY = from TimeRangeMDMY e in Enum.GetValues(typeof(TimeRangeMDMY))
                           select new
                           {
                               ID = (int)e,
                               Name = e.ToString()
                           };

            ViewBag.EnumList = new SelectList(timeRangeMDMY, "ID", "Name");
            deviceView.TimeRange.Range = 60; // default time range, one minute, one day, one month, one year


            deviceView.SensorDataJson = RepoFactory.GetApi().SensorDataGet(jwtKey, idDevice, timeRange, timeMDMY, buildReport).Result;
            

            return View(deviceView);
        }

        public ActionResult Report(int? idDevice, int? idSensorDataReport = 0)
        {

            //HANDLE SESSION
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            if (jwtKey == null || JwtTokenProvider.ValidateToken(jwtKey) == null) { return RedirectToAction("Index", "Login"); }

            DeviceView? deviceView = new DeviceView();
            deviceView.Device = RepoFactory.GetApi().DeviceGet(jwtKey, idDevice, null, null).Result;




            deviceView.SensorDataReport = RepoFactory.GetApi().SensorDataReportGet(jwtKey, idDevice, idSensorDataReport, 0).Result;

            

            if (deviceView.SensorDataReport.Any() == false)
            {
                IList<SensorDataReport> emptyList = new List<SensorDataReport>();
                SensorDataReport empty = new SensorDataReport();
                empty.IDSensorDataReport = idSensorDataReport;
                empty.DeviceID = idDevice;
                empty.ReportName = "No reports";
                emptyList.Add(empty);

                deviceView.SensorDataReport = emptyList;
            }

            if (idSensorDataReport > 0)
            {
                IEnumerable<SensorDataReport> dataResult = RepoFactory.GetApi().SensorDataReportGet(jwtKey, idDevice, idSensorDataReport, 1).Result;
                deviceView.SensorDataJson = dataResult.First().SensorData;
            }


            //SensorDataReport singleReport = deviceView.SensorDataReport.First();
            //deviceView.SensorDataJson = singleReport.SensorData;

            return View(deviceView);
        }

    }
}
