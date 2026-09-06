using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    /// Roadmap #251 modality B - create/list/delete fully virtual devices; the actual simulation loop runs server-side (Agrumy.Api's VirtualDeviceRunnerBackgroundService), this page only manages which devices exist.
    [Authorize(Roles = RoleNames.SimulationManagers)]
    public class SimulationController(IApi api) : Controller
    {
        public async Task<ActionResult> Index()
        {
            IList<int> ids = await api.SimulationDeviceList();
            var devices = new List<DeviceDto>();
            foreach (int id in ids)
            {
                devices.Add(await api.DeviceGet(id));
            }
            return View(devices);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create()
        {
            await api.SimulationDeviceCreate();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Delete(int idDevice)
        {
            await api.SimulationDeviceDelete(idDevice);
            return RedirectToAction(nameof(Index));
        }
    }
}
