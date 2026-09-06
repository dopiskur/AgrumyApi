using api.Dal.Interface;
using api.Models;

namespace api.Commands
{
    public enum ManualActuateOutcome
    {
        Success,
        TargetNotFound,
        UnsupportedFunction,
        InvalidTargetMetric,
        MissingMaxRunSeconds,
    }

    public sealed record ManualActuateResult(ManualActuateOutcome Outcome, IReadOnlyList<int> AffectedDeviceIds, string? Message = null);

    /// Roadmap #219 - target resolution/fan-out (a Zone's one controller, or every controller across a Unit's zones), validation, and the ExpiresAtUtc safety-cap math; no background worker, DeviceConfigBuilder reads the resulting rows lazily on each device's next poll.
    public sealed class ManualActuateService(IDeviceUnitRepository unitRepo)
    {
        /// Heating->Temperature only, Ventilation->Temperature or Humidity, WaterPump->Moisture (soil moisture - see AgrumyFirmware's sensor_analog_moist) only - roadmap #219's explicit per-function allowed subset.
        private static readonly Dictionary<RelayFunction, SensorMetric[]> AllowedTargetMetrics = new()
        {
            [RelayFunction.Heating] = [SensorMetric.Temperature],
            [RelayFunction.Ventilation] = [SensorMetric.Temperature, SensorMetric.Humidity],
            [RelayFunction.WaterPump] = [SensorMetric.Moisture],
        };

        private static int? MaxRunSecondsForFunction(DeviceUnitZone zone, RelayFunction function) => function switch
        {
            RelayFunction.Heating => zone.HeatingMaxRunSeconds,
            RelayFunction.Ventilation => zone.VentilationMaxRunSeconds,
            RelayFunction.WaterPump => zone.WaterPumpMaxRunSeconds,
            _ => null,
        };

        public async Task<ManualActuateResult> StartForZoneAsync(int idDeviceUnitZone, ManualActuateRequest request)
        {
            Device? controller = await unitRepo.DeviceUnitZoneGetControllerAsync(idDeviceUnitZone);
            if (controller?.IDDevice is not int deviceId)
            {
                return new ManualActuateResult(ManualActuateOutcome.TargetNotFound, [], $"Zone {idDeviceUnitZone} has no controller assigned.");
            }
            DeviceUnitZone? zone = await unitRepo.DeviceUnitZoneGetByIdAsync(idDeviceUnitZone);
            if (zone == null)
            {
                return new ManualActuateResult(ManualActuateOutcome.TargetNotFound, [], $"Zone {idDeviceUnitZone} not found.");
            }
            return await StartForTargetsAsync([(deviceId, zone)], request);
        }

        /// Fans out to every zone under the unit that has a controller - a zone with no controller is simply skipped, not an error (same "absent zones are fine" reasoning as CommandQueueService's Unit fan-out for ScanForDevices).
        public async Task<ManualActuateResult> StartForUnitAsync(int idDeviceUnit, ManualActuateRequest request)
        {
            IList<Device> controllers = await unitRepo.DeviceUnitGetControllersAsync(idDeviceUnit);
            if (controllers.Count == 0)
            {
                return new ManualActuateResult(ManualActuateOutcome.TargetNotFound, [], $"Unit {idDeviceUnit} has no controllers across any of its zones.");
            }
            var targets = new List<(int DeviceId, DeviceUnitZone Zone)>();
            foreach (Device controller in controllers)
            {
                if (controller.IDDevice is not int deviceId || controller.DeviceUnitZoneID is not int idZone)
                {
                    continue;
                }
                DeviceUnitZone? zone = await unitRepo.DeviceUnitZoneGetByIdAsync(idZone);
                if (zone != null)
                {
                    targets.Add((deviceId, zone));
                }
            }
            if (targets.Count == 0)
            {
                return new ManualActuateResult(ManualActuateOutcome.TargetNotFound, [], $"Unit {idDeviceUnit} has no controllers across any of its zones.");
            }
            return await StartForTargetsAsync(targets, request);
        }

        public async Task StopAsync(int idDeviceUnitZone, RelayFunction relayFunction)
        {
            Device? controller = await unitRepo.DeviceUnitZoneGetControllerAsync(idDeviceUnitZone);
            if (controller?.IDDevice is int deviceId)
            {
                await unitRepo.ManualOverrideStopAsync(deviceId, relayFunction);
                await unitRepo.DeviceUnitZoneConfigVersionBumpAsync(idDeviceUnitZone);
            }
        }

        private async Task<ManualActuateResult> StartForTargetsAsync(List<(int DeviceId, DeviceUnitZone Zone)> targets, ManualActuateRequest request)
        {
            if (!AllowedTargetMetrics.TryGetValue(request.RelayFunction, out SensorMetric[]? allowedMetrics))
            {
                return new ManualActuateResult(ManualActuateOutcome.UnsupportedFunction, [], $"{request.RelayFunction} cannot be manually actuated.");
            }
            if (request.Mode == ManualOverrideMode.Target && (request.TargetMetric is not SensorMetric metric || !allowedMetrics.Contains(metric)))
            {
                return new ManualActuateResult(ManualActuateOutcome.InvalidTargetMetric, [],
                    $"{request.RelayFunction} Target mode only accepts {string.Join(" or ", allowedMetrics)}.");
            }

            DateTime utcNow = DateTime.UtcNow;
            var affected = new List<int>();
            var zonesToBump = new HashSet<int>();
            bool anySkippedForMissingCap = false;

            // Runs the full loop regardless of a per-target skip - a Unit-level fan-out must not abandon zones already processed (and their pending ConfigVersion bump below) just because a LATER zone in the same batch lacks Target mode's required MaxRunSeconds.
            foreach (var (deviceId, zone) in targets)
            {
                int? maxRunSeconds = MaxRunSecondsForFunction(zone, request.RelayFunction);
                DateTime expiresAtUtc;
                if (request.Mode == ManualOverrideMode.Duration)
                {
                    int requestedSeconds = Math.Max(1, request.DurationSeconds ?? 0);
                    int cappedSeconds = maxRunSeconds is int cap && cap > 0 ? Math.Min(requestedSeconds, cap) : requestedSeconds;
                    expiresAtUtc = utcNow.AddSeconds(cappedSeconds);
                }
                else
                {
                    // Target mode has no natural self-cap (a broken/blocked sensor could otherwise run it forever) - MaxRunSeconds is mandatory here, not just an optional extra ceiling like it is for Duration mode.
                    if (maxRunSeconds is not int cap || cap <= 0)
                    {
                        anySkippedForMissingCap = true;
                        continue;
                    }
                    expiresAtUtc = utcNow.AddSeconds(cap);
                }

                await unitRepo.ManualOverrideStartAsync(new DeviceManualOverride
                {
                    DeviceID = deviceId,
                    TenantID = zone.TenantID ?? 0,
                    RelayFunction = request.RelayFunction,
                    Mode = request.Mode,
                    StartedAtUtc = utcNow,
                    ExpiresAtUtc = expiresAtUtc,
                    TargetMetric = request.Mode == ManualOverrideMode.Target ? request.TargetMetric : null,
                    TargetThreshold = request.Mode == ManualOverrideMode.Target ? request.TargetThreshold : null,
                    TargetHysteresis = request.Mode == ManualOverrideMode.Target ? request.TargetHysteresis : null,
                });
                affected.Add(deviceId);
                if (zone.IDDeviceUnitZone is int idZone)
                {
                    zonesToBump.Add(idZone);
                }
            }

            if (affected.Count == 0 && anySkippedForMissingCap)
            {
                return new ManualActuateResult(ManualActuateOutcome.MissingMaxRunSeconds, [],
                    $"{request.RelayFunction} needs a MaxRunSeconds safety limit configured before Target mode can be used - none of the targeted zone(s) have one set.");
            }

            foreach (int idZone in zonesToBump)
            {
                await unitRepo.DeviceUnitZoneConfigVersionBumpAsync(idZone);
            }

            return new ManualActuateResult(ManualActuateOutcome.Success, affected,
                anySkippedForMissingCap ? $"Started on {affected.Count} of {targets.Count} zone(s) - the rest have no MaxRunSeconds configured for {request.RelayFunction} Target mode." : null);
        }
    }
}
