using api.Models;

namespace api.ViewModels
{
    /// <summary>Roadmap #82: the "Add Controller"/"Add Sensor" device picker - which of the two the
    /// caller clicked determines both the unassigned-device filter and the button/heading copy.</summary>
    public class AssignPickerViewModel
    {
        public int IDDeviceUnitZone { get; set; }
        public bool ControllerCapable { get; set; }
        public IList<Device> Devices { get; set; } = new List<Device>();
    }
}
