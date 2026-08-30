namespace api.Models
{
    public class ServerConfig
    {
        public int? IDServerConfig { get; set; }
        public string? ServerConfigName { get; set; }
        public string? ConfigKey { get; set; }
        public int? PortHTTP { get; set; }
        public int? PortHTTPS { get; set; }

        // Server-wide defaults, seeded from appsettings.json ServerConfig:Hysteresis on first
        // read (or on every startup when ServerConfig:Reload is true), then editable at runtime
        // via this admin settings page - no restart needed after the initial seed. Copied onto a
        // new device's DeviceConfigController row when the device is created; per-device values
        // are then independently editable under Device -> Controller.
        public double? WaterLevelHysteresis { get; set; }
        public double? TemperatureHysteresis { get; set; }
        public double? HumidityHysteresis { get; set; }
        public double? LightHysteresis { get; set; }

        // Roadmap #28: a device repeating the identical DeviceEventType within this many minutes
        // of its last one is ignored server-side rather than stored - same seed/reload/admin-edit
        // pattern as the hysteresis fields above.
        public int? EventDedupeMinutes { get; set; }
    }
}
