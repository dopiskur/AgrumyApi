using api.Models;

namespace api.ViewModels
{
    /// One function's row in _ManualActuateFunction.cshtml (roadmap #219). Zone-scoped: Target mode is only offered when MaxRunSeconds is configured (it has no other self-cap), and Active/Stop reflects that one zone's real state. Unit-scoped (IsUnitLevel): a fan-out trigger with no single "active" state to show - Target mode is always offered, individual zones lacking MaxRunSeconds are silently skipped server-side (api.Commands.ManualActuateService), surfaced only in the post-submit message.
    public class ManualActuateFunctionViewModel
    {
        /// IDDeviceUnitZone normally, or IDDeviceUnit when IsUnitLevel.
        public required int ScopeId { get; init; }
        public bool IsUnitLevel { get; init; }
        public required RelayFunction RelayFunction { get; init; }
        public required string Label { get; init; }
        public int? MaxRunSeconds { get; init; }
        public required IList<SensorMetric> AllowedTargetMetrics { get; init; }
        public DeviceManualOverride? Active { get; init; }
        public string DisplayTimeZone { get; init; } = "UTC";
    }
}
