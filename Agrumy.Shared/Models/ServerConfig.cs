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

        // Server-wide defaults, seeded from appsettings.json ServerConfig:Hysteresis on first read
        // (or on every startup when ServerConfig:Reload is true), then editable at runtime. Copied
        // onto a new device's DeviceConfigController row when created; per-device values are then
        // independently editable under Device -> Controller.
        public double? WaterLevelHysteresis { get; set; }
        public double? TemperatureHysteresis { get; set; }
        public double? HumidityHysteresis { get; set; }
        public double? LightHysteresis { get; set; }

        // Low-battery alert threshold/hysteresis (percent, from the latest sensorData.Battery
        // reading), for LowBatteryAlertEvaluator's periodic sweep - no per-device override since an
        // alert threshold has no reason to differ device-to-device. Fires when Battery <=
        // BatteryLowThreshold, clears once Battery >= BatteryLowThreshold + BatteryLowHysteresis.
        public double? BatteryLowThreshold { get; set; }
        public double? BatteryLowHysteresis { get; set; }

        // WaterPump-only device-side hard safety limits (seconds). Null/0 disables either one.
        // Enforced device-side (AgrumyFirmware's ActuatorController::applyWaterPumpSafetyLimits),
        // independent of whichever control mode decided the pump should run. Per-device override
        // via DeviceConfigController.WaterPumpMaxRunSeconds/WaterPumpCooldownSeconds.
        public int? WaterPumpMaxRunSeconds { get; set; }
        public int? WaterPumpCooldownSeconds { get; set; }

        // A device repeating the identical DeviceEventType within this many minutes of its last one
        // is ignored server-side rather than stored.
        public int? EventDedupeMinutes { get; set; }

        // Minimum minutes between "resend activation email" requests for the same user - default 10.
        public int? ActivationResendCooldownMinutes { get; set; }

        // Default 10, hard ceiling 32 (AgrumyFirmware DeviceModel.h's MAX_RULES) - see ServerConfigApiController.Update for the enforced bound.
        public int? MaxRulesPerZone { get; set; }

        // When true, UserRegistration is allowed to create a brand new tenant for a name it doesn't
        // recognize instead of rejecting the registration. Non-nullable: bool? here would make
        // asp-for render a text box instead of a checkbox.
        [Display(Name = "Allow self-service tenant creation")]
        public bool AllowSelfServiceTenantCreation { get; set; }

        // Gates the Tenant Management menu item (visible only when this is true AND the caller is
        // Global admin - see _Layout.cshtml) - a second, independent condition on top of the role
        // check, so a fresh install doesn't expose cross-tenant management by default.
        [Display(Name = "Enable Tenant Management page")]
        public bool TenantManagementEnabled { get; set; }

        // Single install-wide IANA zone id (e.g. "Europe/Zagreb") schedule-mode relay windows are
        // evaluated against. Null = not configured yet - BuildDeviceConfigAsync then sends every
        // device UtcOffsetSeconds=0 (UTC) rather than failing.
        [Display(Name = "Schedule time zone")]
        public string? ScheduleTimeZone { get; set; }

        // Where firmware comes from - see api.Models.FirmwareSource for what each mode means.
        // GitHub is the default so a fresh install needs no setup.
        // String on the wire: Refit's default serializer writes enums as their names. Admin-only
        // DTO, not part of the firmware contract, so this does not touch the raw-int convention
        // devices rely on.
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

        // Days of sensorData history to keep automatically. PostgreSQL/TimescaleDB installs enforce
        // this through add_retention_policy; MariaDB/MySQL installs enforce it through
        // SensorDataRetentionBackgroundService's daily purge - one shared value, two mechanisms.
        // Null/0 = no automatic retention.
        [Display(Name = "Sensor data retention (days)")]
        public int? SensorDataRetentionDays { get; set; }

        // Install-wide location OpenWeatherMap forecasts are pulled for. Null = not configured yet,
        // WeatherBackgroundService stays inert rather than failing loudly.
        [Display(Name = "Latitude")]
        public double? WeatherLocationLat { get; set; }
        [Display(Name = "Longitude")]
        public double? WeatherLocationLon { get; set; }

        // Admin-editable poll cadence. WeatherBackgroundService itself ticks once a minute (a
        // fixed, cheap DB read) and only actually calls the API once this many minutes have elapsed
        // since WeatherCheckedAtUtc - see that field's remarks for why this makes the interval
        // live-editable without an app restart.
        [Display(Name = "Forecast poll interval (minutes)")]
        public int? WeatherPollIntervalMinutes { get; set; }

        // Rain-probability percentage (OpenWeatherMap's "pop" field, 0-100) at or above which
        // WeatherEvaluator sets WeatherRainPredicted.
        [Display(Name = "Rain-skip threshold (%)")]
        public double? WeatherRainSkipThreshold { get; set; }

        // WeatherEvaluator's last computed result - read-only display on the Server Settings page,
        // NOT settable via ServerConfigApiController.Update; only WeatherEvaluator writes them,
        // through the narrow ServerConfigWeatherStateSetAsync, so a stale admin form post can never
        // clobber a fresher reading.
        [Display(Name = "Rain predicted")]
        public bool WeatherRainPredicted { get; set; }
        [Display(Name = "Forecast last checked")]
        public DateTime? WeatherCheckedAtUtc { get; set; }
    }

    /// <summary>The only field of <see cref="ServerConfig"/> a pre-login, unauthenticated page is
    /// allowed to see - Register uses this to decide whether to show the "create a new tenant"
    /// option, without needing the admin-only /api/ServerConfig.</summary>
    public class PublicServerConfig
    {
        public bool AllowSelfServiceTenantCreation { get; set; }
    }
}
