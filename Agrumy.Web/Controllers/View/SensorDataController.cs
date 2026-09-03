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
            deviceView.TimeRange!.Range = timeRange;

            ViewBag.EnumList = new SelectList(
                Enum.GetValues<TimeRangeMDMY>().Select(e => new { ID = (int)e, Name = e.ToString() }),
                "ID", "Name", timeMDMY);
            ViewBag.TimeMDMY = timeMDMY;

            deviceView.SensorDataJson = await api.SensorDataGet(idDevice, timeRange, timeMDMY, 0);

            // Chart x-axis shows the user's local time; storage stays UTC.
            string? timeZone = (await api.UserGetSelf()).TimeZone;
            deviceView.SensorDataJson = SensorDataTimeLocalizer.LocalizeDates(deviceView.SensorDataJson, timeZone);
            ViewBag.DisplayTimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone;

            return View(deviceView);
        }

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

            // Reports store UTC snapshots; convert for display.
            string? timeZone = (await api.UserGetSelf()).TimeZone;
            deviceView.SensorDataJson = SensorDataTimeLocalizer.LocalizeDates(deviceView.SensorDataJson, timeZone);
            ViewBag.DisplayTimeZone = string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone;

            return View(deviceView);
        }
    }
}
