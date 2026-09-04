using api.Dal.Interface;
using api.Models;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.API
{
    /// <summary>Server-wide settings. Admin-only; there is exactly one row (id 1), auto-created on first read.</summary>
    [Route("api/ServerConfig")]
    public class ServerConfigApiController(IRepository repo, ICache cache) : ApiControllerBase(repo, cache)
    {
        // These are SERVER-WIDE settings, so Global admin only. The attribute stays at the wider "admin" alias so an account the multi-role migration missed reaches the inline check, where CallerIsGlobalAdmin's legacy fallback (tenant-0 admin) still lets it through.

        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<ServerConfig>> Get()
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Server-wide settings require the Global admin role");
            }
            return Ok(await Repo.ServerConfigGetAsync(1));
        }

        [HttpPut]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult> Update([FromBody] ServerConfig config)
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Server-wide settings require the Global admin role");
            }

            // A bad id would silently degrade every device's schedule mode to UTC (TimeZoneHelper.GetUtcOffsetSeconds' fallback) rather than fail loudly at save time. Blank/null clears it back to "not configured".
            if (!string.IsNullOrWhiteSpace(config.ScheduleTimeZone))
            {
                if (!TimeZoneHelper.TryNormalizeToIana(config.ScheduleTimeZone, out string iana))
                {
                    return BadRequest("Unknown time zone: " + config.ScheduleTimeZone);
                }
                config.ScheduleTimeZone = iana;
            }
            else
            {
                config.ScheduleTimeZone = null;
            }

            // A Custom source with no manifest URL (or a non-http one) would leave every sync failing with a vague error.
            if (!Enum.IsDefined(config.FirmwareSource))
            {
                return BadRequest("Unknown firmware source: " + config.FirmwareSource);
            }
            if (config.FirmwareSource == FirmwareSource.Custom &&
                (!Uri.TryCreate(config.FirmwareCustomRepositoryUrl, UriKind.Absolute, out Uri? customUri) || customUri.Scheme is not ("http" or "https")))
            {
                return BadRequest("Custom firmware source needs an absolute http(s) manifest URL.");
            }
            // Blank = back to the appsettings seed (EfRepository.ServerConfig's ToDto fallback); a value must be owner/name.
            config.FirmwareGitHubRepository = string.IsNullOrWhiteSpace(config.FirmwareGitHubRepository) ? null : config.FirmwareGitHubRepository.Trim().Trim('/');
            if (config.FirmwareGitHubRepository != null && config.FirmwareGitHubRepository.Count(c => c == '/') != 1)
            {
                return BadRequest("GitHub repository must be in owner/name form.");
            }

            // The server-wide default pair new devices are seeded with - same bound DeviceApiController.DeviceConfigControllerUpdate enforces on a per-device override.
            if (!SafetyLimitValidation.IsValid(config.WaterPumpMaxRunSeconds))
            {
                return BadRequest($"WaterPump max run time must be between 0 (disabled) and {SafetyLimitValidation.MaxReasonableSeconds} seconds.");
            }
            if (!SafetyLimitValidation.IsValid(config.WaterPumpCooldownSeconds))
            {
                return BadRequest($"WaterPump cooldown must be between 0 (disabled) and {SafetyLimitValidation.MaxReasonableSeconds} seconds.");
            }

            // Hard ceiling matches AgrumyFirmware DeviceModel.h's MAX_RULES - above it the firmware silently drops extra rules.
            if (config.MaxRulesPerZone is < 1 or > 32)
            {
                return BadRequest("Max rules per zone must be between 1 and 32.");
            }

            // Negative retention is meaningless; 0/null are both valid ways to say "no automatic retention".
            if (config.SensorDataRetentionDays is < 0)
            {
                return BadRequest("Sensor data retention days must be 0/empty (disabled) or a positive number.");
            }

            // lat/lon are a pair - one set without the other silently sends WeatherEvaluator queries to (lat, 0) or (0, lon).
            if (config.WeatherLocationLat.HasValue != config.WeatherLocationLon.HasValue)
            {
                return BadRequest("Weather latitude and longitude must both be set, or both left empty.");
            }
            if (config.WeatherLocationLat is < -90 or > 90)
            {
                return BadRequest("Weather latitude must be between -90 and 90.");
            }
            if (config.WeatherLocationLon is < -180 or > 180)
            {
                return BadRequest("Weather longitude must be between -180 and 180.");
            }
            if (config.WeatherPollIntervalMinutes is < 1)
            {
                return BadRequest("Weather poll interval must be at least 1 minute.");
            }
            if (config.WeatherRainSkipThreshold is < 0 or > 100)
            {
                return BadRequest("Weather rain-skip threshold must be between 0 and 100 percent.");
            }

            // Negative is meaningless; 0/null both mean "auto-refresh disabled".
            if (config.FirmwareRefreshIntervalHours is < 0)
            {
                return BadRequest("Firmware auto-refresh interval must be 0/empty (disabled) or a positive number of hours.");
            }

            config.IDServerConfig = 1; // single global row - the form never chooses this
            await Repo.ServerConfigUpdateAsync(config);
            return Ok();
        }

        /// <summary>The Register page is anonymous and must not call the admin-only Get() above just to know whether to show a "create a new tenant" field - this exposes only that one flag.</summary>
        [HttpGet("Public")]
        [AllowAnonymous]
        public async Task<ActionResult<PublicServerConfig>> GetPublic()
        {
            ServerConfig config = await Repo.ServerConfigGetAsync(1);
            return Ok(new PublicServerConfig { AllowSelfServiceTenantCreation = config.AllowSelfServiceTenantCreation });
        }
    }
}
