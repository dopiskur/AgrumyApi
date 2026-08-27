namespace api.Models
{
    public class DeviceView
    {
        public Device? Device { get; set; } = new Device();
        public DeviceConfigSensor? DeviceConfigSensor { get; set; }
        public DeviceConfigController? DeviceConfigController { get; set; }
        public IEnumerable<DeviceType>? DeviceType { get; set; }
        public IEnumerable<DeviceTypeService>? DeviceTypeService { get; set; }
        public IEnumerable<DeviceTypeRelay>? DeviceTypeRelay { get; set; }
        public IEnumerable<DeviceTypeSensor>? DeviceTypeSensor { get; set; }

        public IEnumerable<SensorData>? SensorData;
        public IEnumerable<SensorDataReport>? SensorDataReport { get; set; }
        public String? SensorDataJson { get; set; }
        public TimeRange? TimeRange { get; set; } = new TimeRange();
    }
}
