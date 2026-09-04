using api.Dal.Interface;
using api.Models;
using api.Security;
using api.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    /// <summary>Lists registered Agrumy.Relay devices and, for a LoRaGateway
    /// profile, manages its DevEUI-&gt;device mapping table. Relays are install-wide infrastructure
    /// (see IRelayRepository's remarks), so this is Global-Admin-only like Firmware/ServerConfig,
    /// not tenant-scoped like the ordinary Device pages.</summary>
    [Authorize(Roles = RoleNames.GlobalAdmin)]
    public class RelayController(IApi api) : Controller
    {
        public async Task<ActionResult> Index()
        {
            return View(new RelayListViewModel { Relays = await api.RelaysGetAll() });
        }

        public async Task<ActionResult> Mapping(int idRelayDevice)
        {
            Device relay = await api.DeviceGet(idRelayDevice);
            if (relay?.IsRelay != true)
            {
                return NotFound();
            }

            IList<RelayDeviceMapping> mappings = await api.RelayDeviceMappingGetAll(idRelayDevice);
            IList<Device> availableDevices = (await api.DevicesGet()).Where(d => d.IsRelay != true).ToList();

            return View(new RelayMappingViewModel
            {
                Relay = relay,
                Mappings = mappings,
                AvailableDevices = availableDevices,
            });
        }

        [HttpPost]
        public async Task<ActionResult> MappingAdd(int idRelayDevice, string devEUI, int idDevice)
        {
            await api.RelayDeviceMappingAdd(new RelayDeviceMapping
            {
                IDRelayDevice = idRelayDevice,
                DevEUI = devEUI,
                IDDevice = idDevice,
            });
            return RedirectToAction(nameof(Mapping), new { idRelayDevice });
        }

        [HttpPost]
        public async Task<ActionResult> MappingDelete(int idRelayDeviceMapping, int idRelayDevice)
        {
            await api.RelayDeviceMappingDelete(idRelayDeviceMapping, idRelayDevice);
            return RedirectToAction(nameof(Mapping), new { idRelayDevice });
        }
    }
}
