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

        // Roadmap #12: low-battery alert threshold/hysteresis (percent, from the latest
        // sensorData.Battery reading) - same dead-zone principle as the four fields above, but
        // for LowBatteryAlertEvaluator's periodic sweep (roadmap #40 pattern), not on-device
        // relay logic, so there is no per-device DeviceConfigController override: an alert
        // threshold has no reason to differ device-to-device the way a physical relay setpoint
        // does. Fires when Battery <= BatteryLowThreshold, clears (rearms) once Battery >=
        // BatteryLowThreshold + BatteryLowHysteresis - avoids alert-spam flapping right at the edge.
        public double? BatteryLowThreshold { get; set; }
        public double? BatteryLowHysteresis { get; set; }

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

        // Roadmap #39: single install-wide IANA zone id (e.g. "Europe/Zagreb") schedule-mode relay
        // windows (DeviceConfigController.*ScheduleStart/Duration) are evaluated against. Null =
        // not configured yet - BuildDeviceConfigAsync then sends every device UtcOffsetSeconds=0
        // (UTC), so an admin who enables a schedule before setting this just gets UTC-anchored
        // windows rather than a hard failure. One zone for the whole install (not per-device/tenant)
        // is a deliberate v1 simplification - nothing in the roadmap design asked for finer scope,
        // and every fleet observed so far lives in one geographic timezone.
        [Display(Name = "Schedule time zone")]
        public string? ScheduleTimeZone { get; set; }

        // Roadmap #94: where firmware comes from - see api.Models.FirmwareSource for what each mode
        // means. GitHub is the default so a fresh install needs no setup; the repository is
        // editable so a fork can point at its own releases.
        // String on the wire: Refit's default serializer writes enums as their names, and the API
        // must read that back (it still accepts the integer form too). Admin-only DTO, not part of
        // the firmware contract, so this does not touch the raw-int convention devices rely on.
        [Display(Name = "Firmware source")]
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        public FirmwareSource FirmwareSource { get; set; }

        [Display(Name = "GitHub repository (owner/name)")]
        public string? FirmwareGitHubRepository { get; set; }

        /// <summary>Custom mode only: absolute URL of a manifest.json in the format
        /// api.Models.FirmwareManifest describes - the .bin URLs inside it may be absolute or
        /// relative to the manifest's own location.</summary>
        [Display(Name = "Custom repository manifest URL")]
        public string? FirmwareCustomRepositoryUrl { get; set; }
    }

    /// <summary>The only two fields of <see cref="ServerConfig"/> a pre-login, unauthenticated page
    /// is allowed to see - roadmap #64's Register view uses this to decide whether to show the
    /// "create a new tenant" option at all, without needing the admin-only /api/ServerConfig.</summary>
    public class PublicServerConfig
    {
        public bool AllowSelfServiceTenantCreation { get; set; }
    }
}
