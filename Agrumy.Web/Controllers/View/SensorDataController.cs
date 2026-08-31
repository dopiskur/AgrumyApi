using api.Dal.Interface;
using api.Models;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace api.Controllers.View
{
    [Authorize]
    public class SensorDataController(IApi api) : Controller
    {
        public async Task<ActionResult> Index(int? idDevice, int? timeRange = 60, int? timeMDMY = 0)
        {
            if (timeRange > 1440)
            {
                timeRange = 1440; // cap at one day of minute-resolution data
            }

            var deviceView = new DeviceView { Device = await api.DeviceGet(idDevice) };
            deviceView.TimeRange!.Range = timeRange; // reflect the selected range back to the form

            ViewBag.EnumList = new SelectList(
                Enum.GetValues<TimeRangeMDMY>().Select(e => new { ID = (int)e, Name = e.ToString() }),
                "ID", "Name", timeMDMY);
            ViewBag.TimeMDMY = timeMDMY; // the report form snapshots the currently displayed period

            deviceView.SensorDataJson = await api.SensorDataGet(idDevice, timeRange, timeMDMY, 0);

            // Roadmap #71 follow-up: chart x-axis shows the user's local time; storage stays UTC.
            string? timeZone = (await api.UserGetSelf()).TimeZone;
            deviceView.SensorDataJson = SensorDataTimeLocalizer.LocalizeDates(deviceView.SensorDataJson, timeZone);
            ViewBag.DisplayTimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone;

            return View(deviceView);
        }

        /// <summary>Report generation writes a sensorDataReport row, so it must be a POST with an
        /// antiforgery token - it used to ride along as a buildReport=1 flag on the GET Index.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> GenerateReport(int? idDevice, int? timeRange = 60, int? timeMDMY = 0)
        {
            if (timeRange > 1440)
            {
                timeRange = 1440;
            }

            await api.SensorDataGet(idDevice, timeRange, timeMDMY, 1);
            TempData["Message"] = "Report generated.";
            return RedirectToAction(nameof(Report), new { idDevice });
        }

        public async Task<ActionResult> Report(int? idDevice, int? idSensorDataReport = 0)
        {
            var deviceView = new DeviceView
            {
                Device = await api.DeviceGet(idDevice),
                SensorDataReport = await api.SensorDataReportGet(idDevice, idSensorDataReport, 0),
            };

            if (!deviceView.SensorDataReport.Any())
            {
                deviceView.SensorDataReport = new List<SensorDataReport>
                {
                    new() { IDSensorDataReport = idSensorDataReport, DeviceID = idDevice, ReportName = "No reports" },
                };
            }

            if (idSensorDataReport > 0)
            {
                var single = await api.SensorDataReportGet(idDevice, idSensorDataReport, 1);
                deviceView.SensorDataJson = single.FirstOrDefault()?.SensorData;
            }

            // Same UTC-to-user-zone display conversion as Index - reports store UTC snapshots.
            string? timeZone = (await api.UserGetSelf()).TimeZone;
            deviceView.SensorDataJson = SensorDataTimeLocalizer.LocalizeDates(deviceView.SensorDataJson, timeZone);
            ViewBag.DisplayTimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone;

            return View(deviceView);
        }
    }
}
