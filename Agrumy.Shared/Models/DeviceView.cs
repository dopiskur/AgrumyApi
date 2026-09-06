namespace api.Models
{
    public class DeviceView
    {
        public DeviceDto? Device { get; set; } = new DeviceDto();
        // Bound ONLY by the Edit form (see DeviceEditForm) - every other DeviceView consumer (Details, Events, EditSensor, EditController) leaves this null and just reads Device for display.
        public DeviceEditForm? DeviceEdit { get; set; }
        public DeviceConfigSensor? DeviceConfigSensor { get; set; }
        public DeviceConfigController? DeviceConfigController { get; set; }
        public IEnumerable<DeviceRole>? DeviceRole { get; set; }
        // Backs the Edit form's "Manual Kit" dropdown.
        public IEnumerable<DeviceType>? DeviceType { get; set; }
        public IEnumerable<DeviceTypeService>? DeviceTypeService { get; set; }
        public IEnumerable<DeviceTypeRelay>? DeviceTypeRelay { get; set; }
        public IEnumerable<DeviceTypeSensor>? DeviceTypeSensor { get; set; }

        public IEnumerable<SensorDataReport>? SensorDataReport { get; set; }
        public String? SensorDataJson { get; set; }
        public TimeRange? TimeRange { get; set; } = new TimeRange();

        public IList<DeviceEvent>? Events { get; set; }
        // Bound only by the Simulation page.
        public DeviceSimulation? DeviceSimulation { get; set; }
    }
}
