using api.BackgroundWorkers;
using api.Dal;
using api.Dal.Interface;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers.API
{
    /// <summary>Roadmap #126: "Optimize Old Data" / "Purge Old Data" - Global admin only, same rule
    /// as ServerConfigApiController since this affects every tenant's telemetry. Both actions
    /// dispatch to BackgroundJobQueue and return 202 immediately rather than holding the HTTP
    /// request open for however long a large table takes to process.</summary>
    [Route("api/DataMaintenance")]
    [Authorize(Roles = "admin")]
    public class DataMaintenanceApiController(
        IRepository repo, ICache cache, AgrumyDbContext db, BackgroundJobQueue jobQueue, ILogger<DataMaintenanceApiController> logger)
        : ApiControllerBase(repo, cache)
    {
        /// <summary>Lets Agrumy.Web decide whether to show the MariaDB-only "shrink files on disk?"
        /// follow-up dialog before it asks the admin to confirm a Purge - Postgres/TimescaleDB's
        /// drop_chunks() always reclaims disk space with no extra step, so there is no question to
        /// ask on that provider.</summary>
        [HttpGet("Provider")]
        public ActionResult<DataMaintenanceProviderInfo> GetProvider()
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Server-wide data maintenance requires the Global admin role");
            }
            return Ok(new DataMaintenanceProviderInfo { IsMySql = !db.Database.IsNpgsql() });
        }

        [HttpPost("Optimize")]
        public ActionResult Optimize([FromBody] DataMaintenanceRequest request)
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Server-wide data maintenance requires the Global admin role");
            }
            if (!DataMaintenanceThresholds.AllowedDays.Contains(request.OlderThanDays))
            {
                return BadRequest("Unsupported threshold.");
            }

            DateTime cutoffUtc = DateTime.UtcNow.AddDays(-request.OlderThanDays);
            int olderThanDays = request.OlderThanDays;
            jobQueue.Enqueue(async (services, ct) =>
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Optimize Old Data started (older than {Days} days, cutoff {Cutoff:u}).", olderThanDays, cutoffUtc);
                }
                await services.GetRequiredService<ISensorDataRepository>().OptimizeOldSensorDataAsync(cutoffUtc, ct);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Optimize Old Data finished (cutoff {Cutoff:u}).", cutoffUtc);
                }
            });

            return Accepted();
        }

        [HttpPost("Purge")]
        public ActionResult Purge([FromBody] DataPurgeRequest request)
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Server-wide data maintenance requires the Global admin role");
            }
            if (!DataMaintenanceThresholds.AllowedDays.Contains(request.OlderThanDays))
            {
                return BadRequest("Unsupported threshold.");
            }
            // Enforced here too, not just in the Web form - the API is reachable directly, so the
            // typed-confirmation gate (roadmap #126, "at least as strict as #92") must hold server-side.
            if (request.ConfirmationPhrase != DataPurgeRequest.RequiredPhrase)
            {
                return BadRequest($"Type \"{DataPurgeRequest.RequiredPhrase}\" to confirm this destructive action.");
            }

            DateTime cutoffUtc = DateTime.UtcNow.AddDays(-request.OlderThanDays);
            int olderThanDays = request.OlderThanDays;
            bool shrinkAfterPurge = request.ShrinkAfterPurge;
            jobQueue.Enqueue(async (services, ct) =>
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Purge Old Data started (older than {Days} days, cutoff {Cutoff:u}, shrink={Shrink}).",
                        olderThanDays, cutoffUtc, shrinkAfterPurge);
                }
                await services.GetRequiredService<ISensorDataRepository>().PurgeOldSensorDataAsync(cutoffUtc, shrinkAfterPurge, ct);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Purge Old Data finished (cutoff {Cutoff:u}).", cutoffUtc);
                }
            });

            return Accepted();
        }
    }
}
