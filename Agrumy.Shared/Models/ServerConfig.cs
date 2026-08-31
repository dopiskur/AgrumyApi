using System.ComponentModel.DataAnnotations;

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

        // Roadmap #24: minimum minutes between "resend activation email" requests for the same
        // user - default 10, admin-editable (same seed/reload pattern as the fields above).
        public int? ActivationResendCooldownMinutes { get; set; }

        // Roadmap #64: off by default. When true, UserRegistration is allowed to create a brand
        // new tenant for a name it doesn't recognize instead of rejecting the registration.
        // Non-nullable: the row always carries a concrete value (seeded on first read, see
        // EfRepository.ServerConfigGetAsync) - bool? here made asp-for render a text box
        // ("True"/"False") instead of a checkbox, since the InputTagHelper only auto-detects
        // a checkbox for a non-nullable bool.
        [Display(Name = "Allow self-service tenant creation")]
        public bool AllowSelfServiceTenantCreation { get; set; }
    }

    /// <summary>The only two fields of <see cref="ServerConfig"/> a pre-login, unauthenticated page
    /// is allowed to see - roadmap #64's Register view uses this to decide whether to show the
    /// "create a new tenant" option at all, without needing the admin-only /api/ServerConfig.</summary>
    public class PublicServerConfig
    {
        public bool AllowSelfServiceTenantCreation { get; set; }
    }
}
