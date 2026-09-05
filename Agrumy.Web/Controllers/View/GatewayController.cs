using api.Dal.Interface;
using api.Models;
using api.Security;
using api.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    /// Lists registered gateways and manages a LoRaGateway's DevEUI-&gt;device mapping; gateways are install-wide infrastructure (IGatewayRepository), so this is Global-Admin-only, not tenant-scoped.
    [Authorize(Roles = RoleNames.GlobalAdmin)]
    public class GatewayController(IApi api) : Controller
    {
        public async Task<ActionResult> Index()
        {
            return View(new GatewayListViewModel { Gateways = await api.GatewaysGetAll() });
        }

        public async Task<ActionResult> Mapping(int idGatewayDevice)
        {
            DeviceDto gateway = await api.DeviceGet(idGatewayDevice);
            if (gateway?.IsGateway != true)
            {
                return NotFound();
            }

            IList<GatewayDeviceMapping> mappings = await api.GatewayDeviceMappingGetAll(idGatewayDevice);
            IList<DeviceDto> availableDevices = (await api.DevicesGet()).Where(d => d.IsGateway != true).ToList();

            return View(new GatewayMappingViewModel
            {
                Gateway = gateway,
                Mappings = mappings,
                AvailableDevices = availableDevices,
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> MappingAdd(int idGatewayDevice, string devEUI, int idDevice)
        {
            await api.GatewayDeviceMappingAdd(new GatewayDeviceMapping
            {
                IDGatewayDevice = idGatewayDevice,
                DevEUI = devEUI,
                IDDevice = idDevice,
            });
            return RedirectToAction(nameof(Mapping), new { idGatewayDevice });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> MappingDelete(int idGatewayDeviceMapping, int idGatewayDevice)
        {
            await api.GatewayDeviceMappingDelete(idGatewayDeviceMapping, idGatewayDevice);
            return RedirectToAction(nameof(Mapping), new { idGatewayDevice });
        }
    }
}
