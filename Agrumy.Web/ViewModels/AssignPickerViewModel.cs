using api.Models;

namespace api.ViewModels
{
    public class AssignPickerViewModel
    {
        public int IDDeviceUnitZone { get; set; }
        public bool ControllerCapable { get; set; }
        public IList<Device> Devices { get; set; } = new List<Device>();
    }
}
