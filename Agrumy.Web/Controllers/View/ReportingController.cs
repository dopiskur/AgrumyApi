using api.Dal.Interface;
using api.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    /// Cross-device list of every SensorDataReport in scope - the per-device Report tab (SensorDataController.Report) covers one device, this is the overview across all of them.
    [Authorize]
    public class ReportingController(IApi api) : Controller
    {
        public async Task<ActionResult> Index()
        {
            var reports = await api.SensorDataReportGet(null, 0, 0);
            var devices = (await api.DevicesGet()).ToDictionary(d => d.IDDevice ?? 0, d => d.DeviceName ?? $"Device {d.IDDevice}");

            var rows = reports
                .OrderByDescending(r => r.DateGenerated)
                .Select(r => new ReportingRow
                {
                    Report = r,
                    DeviceName = devices.TryGetValue(r.DeviceID ?? 0, out var name) ? name : $"Device {r.DeviceID}",
                })
                .ToList();

            return View(rows);
        }
    }
}
