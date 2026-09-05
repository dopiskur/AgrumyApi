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

        // Server-wide defaults seeded from ServerConfig:Hysteresis, then runtime-editable; copied onto a new device's DeviceConfigController row, which is independently editable afterward under Device -> Controller.
        public double? WaterLevelHysteresis { get; set; }
        public double? TemperatureHysteresis { get; set; }
        public double? HumidityHysteresis { get; set; }
        public double? LightHysteresis { get; set; }

        // LowBatteryAlertEvaluator's threshold/hysteresis (percent of sensorData.Battery), no per-device override; fires at Battery<=Threshold, clears at Battery>=Threshold+Hysteresis.
        public double? BatteryLowThreshold { get; set; }
        public double? BatteryLowHysteresis { get; set; }

        // WaterPump-only hard safety limits (seconds, null/0 disables); enforced device-side by ActuatorController::applyWaterPumpSafetyLimits regardless of control mode, overridable per-device via DeviceConfigController.
        public int? WaterPumpMaxRunSeconds { get; set; }
        public int? WaterPumpCooldownSeconds { get; set; }

        // A device repeating the identical DeviceEventType within this many minutes is ignored server-side rather than stored.
        public int? EventDedupeMinutes { get; set; }

        // Gates whether non-critical problem events (crash/auth/sync/OTA) turn a Unit/Zone Orange at all - see api.Dal.EfRepository.ComputeStatus.
        [Display(Name = "Alert on non-critical device problems")]
        public bool ProblemEventAlertsEnabled { get; set; } = true;

        // How long an un-acknowledged problem event keeps a Unit/Zone Orange, clamped to {1,6,12,24,48} by ServerConfigApiController.Update; acknowledging clears it immediately regardless.
        [Display(Name = "Problem alert expiry (hours)")]
        public int ProblemEventExpiryHours { get; set; } = 24;

        // Minimum minutes between "resend activation email" requests for the same user - default 10.
        public int? ActivationResendCooldownMinutes { get; set; }

        // Default 10, hard ceiling 32 (AgrumyFirmware DeviceModel.h's MAX_RULES) - see ServerConfigApiController.Update for the enforced bound.
        public int? MaxRulesPerZone { get; set; }

        // Allows UserRegistration to create an unrecognized tenant name instead of rejecting; non-nullable because bool? would render asp-for as a text box, not a checkbox.
        [Display(Name = "Allow self-service tenant creation")]
        public bool AllowSelfServiceTenantCreation { get; set; }

        // Gates the Tenant Management menu item alongside the GlobalAdmin role check (_Layout.cshtml), so a fresh install doesn't expose cross-tenant management by default.
        [Display(Name = "Enable Tenant Management page")]
        public bool TenantManagementEnabled { get; set; }

        // Install-wide IANA zone id schedule-mode windows evaluate against; null sends every device UtcOffsetSeconds=0 (UTC) via BuildDeviceConfigAsync rather than failing.
        [Display(Name = "Schedule time zone")]
        public string? ScheduleTimeZone { get; set; }

        // Where firmware comes from (api.Models.FirmwareSource); GitHub defaults for zero-setup installs. String on the wire (Refit enum-as-name) since this admin DTO doesn't touch the device-facing raw-int convention.
        [Display(Name = "Firmware source")]
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        public FirmwareSource FirmwareSource { get; set; }

        [Display(Name = "GitHub repository (owner/name)")]
        public string? FirmwareGitHubRepository { get; set; }

        /// Custom mode only: absolute URL of a manifest.json in api.Models.FirmwareManifest's format - its .bin URLs may be absolute or relative to the manifest's own location.
        [Display(Name = "Custom repository manifest URL")]
        public string? FirmwareCustomRepositoryUrl { get; set; }

        // Null/0 disables auto-refresh; FirmwareCatalogRefreshBackgroundService re-reads this live value every minute, same pattern as WeatherPollIntervalMinutes.
        [Display(Name = "Auto-refresh catalog every (hours)")]
        public int? FirmwareRefreshIntervalHours { get; set; }

        // Written only by FirmwareCatalogRefreshEvaluator (ServerConfigFirmwareRefreshStateSetAsync), same reasoning as WeatherCheckedAtUtc, so a stale admin form post can't clobber it.
        [Display(Name = "Catalog last auto-refreshed")]
        public DateTime? FirmwareLastRefreshedAtUtc { get; set; }

        // Days of sensorData history to auto-purge - Postgres/TimescaleDB via add_retention_policy, MariaDB/MySQL via SensorDataRetentionBackgroundService's daily purge; null/0 disables it.
        [Display(Name = "Sensor data retention (days)")]
        public int? SensorDataRetentionDays { get; set; }

        // Install-wide location OpenWeatherMap forecasts are pulled for; null leaves WeatherBackgroundService inert rather than failing loudly.
        [Display(Name = "Latitude")]
        public double? WeatherLocationLat { get; set; }
        [Display(Name = "Longitude")]
        public double? WeatherLocationLon { get; set; }

        // Admin-editable poll cadence - WeatherBackgroundService ticks every minute but only calls the API once this many minutes have elapsed since WeatherCheckedAtUtc, making the interval live-editable without a restart.
        [Display(Name = "Forecast poll interval (minutes)")]
        public int? WeatherPollIntervalMinutes { get; set; }

        // Rain-probability percentage (OpenWeatherMap's "pop" field) at or above which WeatherEvaluator sets WeatherRainPredicted.
        [Display(Name = "Rain-skip threshold (%)")]
        public double? WeatherRainSkipThreshold { get; set; }

        // WeatherEvaluator's last result - read-only on Server Settings, written only through ServerConfigWeatherStateSetAsync so a stale admin form post can't clobber a fresher reading.
        [Display(Name = "Rain predicted")]
        public bool WeatherRainPredicted { get; set; }
        [Display(Name = "Forecast last checked")]
        public DateTime? WeatherCheckedAtUtc { get; set; }

        // Gates the Relay Devices admin page (_Layout.cshtml, same pattern as TenantManagementEnabled) and whether RelayApiController accepts Batch calls at all.
        [Display(Name = "Enable Agrumy.Relay support")]
        public bool RelayEnabled { get; set; }

        [Display(Name = "Relay mode")]
        [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        public RelayMode RelayMode { get; set; }

        // Aggregated mode only (see RelayBatchResponse); clamped 10-300 by ServerConfigApiController.Update, same pattern as MaxRulesPerZone.
        [Display(Name = "Aggregated wait window (seconds)")]
        public int RelayWaitWindowSeconds { get; set; } = 30;
    }

    /// The only ServerConfig field a pre-login, unauthenticated page may see - Register uses it to decide whether to show "create a new tenant" without needing the admin-only /api/ServerConfig.
    public class PublicServerConfig
    {
        public bool AllowSelfServiceTenantCreation { get; set; }
    }
}
