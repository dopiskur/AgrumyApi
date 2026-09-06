using Microsoft.AspNetCore.Mvc;

namespace api.Models
{
    /// A physical/logical space (e.g. a greenhouse) containing DeviceUnitZones; TenantID null only means the shared IDDeviceUnit=0 "Default" sentinel every unzoned device points at.
    public class DeviceUnit
    {
        [HiddenInput(DisplayValue = true)]
        public int? IDDeviceUnit { get; set; }
        public int? TenantID { get; set; }
        public string? DeviceUnitName { get; set; }
    }

    /// A growing zone within one DeviceUnit - "one zone = one controller" at most, may be sensor-only; TenantID is denormalized from DeviceUnit so a zone query needs no join to check ownership.
    public class DeviceUnitZone
    {
        [HiddenInput(DisplayValue = true)]
        public int? IDDeviceUnitZone { get; set; }
        public int? TenantID { get; set; }
        public int DeviceUnitID { get; set; }
        public string? DeviceUnitZoneName { get; set; }

        // WaterPump-only hard safety ceiling, not a Rule - applied by the device after a rule already decided WaterPump should run; seeded from AgrumySettings on creation.
        public int? WaterPumpMaxRunSeconds { get; set; }
        public int? WaterPumpCooldownSeconds { get; set; }

        // Per-zone opt-in, not a global switch; combined server-side with ServerConfig.WeatherRainPredicted into DeviceConfigController.SkipWaterPumpForRain.
        public bool SkipWaterPumpWhenRainPredicted { get; set; }
    }

    /// Relay function a DeviceUnitZoneRule targets, same numeric convention as deviceTypeRelay seed rows; kept as a plain int on the wire (not this enum) so firmware can parse it as a number without JsonStringEnumConverter.
    public enum RelayFunction
    {
        Ventilation = 1,
        Light = 2,
        Heating = 3,
        WaterPump = 4,
    }

    /// Roadmap #343. One entry in POST /api/ControllerData's array - sent every time a relay's on/off state actually CHANGES, not on a fixed interval like SensorData. Universal: a real device pushes this alongside a physical relay flip, a simulated one pushes it alongside its calculated equivalent - same wire shape, same table, same downstream logic either way.
    public class ControllerDataPush
    {
        public RelayFunction RelayFunction { get; set; }
        public bool IsOn { get; set; }
        public DateTime? DateCreated { get; set; }
    }

    /// Current on/off state for one RelayFunction on one device - GET /api/ControllerData's shape, and what DeviceFleetStatus.RelayStates carries.
    public class ControllerDataStatus
    {
        public RelayFunction RelayFunction { get; set; }
        public bool IsOn { get; set; }
        public DateTime? DateChanged { get; set; }
    }

    /// Which measured quantity a Notification-action Threshold condition reads - mirrors SensorAverages' fields (a DeviceUnitZoneRule.RelayFunction implies its metric/direction instead, so Relay-action rules never set this).
    public enum SensorMetric
    {
        Temperature = 1,
        SoilTemperature = 2,
        Humidity = 3,
        Vpd = 4,
        Moisture = 5,
        Light = 6,
        Co2 = 7,
        Tvoc = 8,
        Barometer = 9,
        LiquidPH = 10,
        RainLevel = 11,
        WaterLevel = 12,
        Wind = 13,
    }

    /// What a rule does once its Conditions fold to true - Relay is evaluated on-device (AgrumyFirmware's ActuatorController), Notification is evaluated server-side (api.BackgroundWorkers.RuleNotificationEvaluator) since firmware has no notification capability.
    public enum ActionType
    {
        Relay = 1,
        Notification = 2,
    }

    /// AND/OR between two consecutive Conditions - folded strictly left-to-right ("(A AND B) OR C", never "A AND (B OR C)"), no parentheses/nesting.
    public enum LogicalOperator
    {
        And = 1,
        Or = 2,
    }

    /// Which condition a RuleCondition evaluates - see ThresholdConditionConfig/IntervalConditionConfig/ScheduleConditionConfig/AstronomicalConditionConfig/RuleTriggeredConditionConfig for each type's shape.
    public enum ConditionType
    {
        Threshold = 1,
        Interval = 2,
        Schedule = 3,
        /// Never reaches firmware as-is - api.Devices.AstronomicalRuleResolver compiles it into an effective Schedule rule for today's local date before the config is sent.
        Astronomical = 4,
        /// Only valid on a Notification-action rule - a Relay-action rule fires invisibly on-device, so the server has no way to observe it as a trigger.
        RuleTriggered = 5,
    }

    /// A materialized JsonNode's keys are frozen by whatever options built it - an outer JsonSerializer.Serialize(camelCaseOptions) does NOT re-key it, so every RuleCondition.ConditionConfig read/write must use these exact Options or camelCase drifts to PascalCase.
    public static class ConditionConfigJson
    {
        public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);
    }

    /// One entry in a DeviceUnitZoneRule's flat, left-to-right condition list - Operator is the operator BEFORE this condition, null for the first entry, required otherwise.
    public record RuleCondition(ConditionType ConditionType, System.Text.Json.Nodes.JsonNode? ConditionConfig, LogicalOperator? Operator);

    /// One automation rule at exactly one scope - DeviceUnitZoneID set means Zone scope, DeviceUnitID set means Unit scope, both null means Global (per-tenant: every unit/zone the tenant owns). Several rules at the SAME scope for the same RelayFunction/SensorMetric still OR together; within one rule, Conditions fold left-to-right by their own Operator. A more specific scope's rules for a function/metric fully replace (not merge with) a less specific scope's, resolved server-side (api.Devices.RuleHierarchyResolver) before a Relay rule ever reaches firmware.
    public class DeviceUnitZoneRule
    {
        [HiddenInput(DisplayValue = true)]
        public int? IDDeviceUnitZoneRule { get; set; }
        public int TenantID { get; set; }
        public int? DeviceUnitID { get; set; }
        public int? DeviceUnitZoneID { get; set; }
        public ActionType ActionType { get; set; } = ActionType.Relay;
        /// Required when ActionType is Relay, null when Notification.
        public RelayFunction? RelayFunction { get; set; }
        /// Required when ActionType is Notification (and a Threshold condition needs a metric to read), null when Relay.
        public SensorMetric? SensorMetric { get; set; }
        public IList<RuleCondition> Conditions { get; set; } = [];
        /// Notification-action only; supports {zone}/{value}/{metric} placeholders, substituted by RuleNotificationEvaluator.
        public string? NotificationSubject { get; set; }
        public string? NotificationBody { get; set; }
    }

    /// Threshold+hysteresis. On a Relay-action rule the metric/direction is implicit in the rule's RelayFunction (see AgrumyFirmware's ActuatorController::evaluateCondition); on a Notification-action rule the direction is always "turns on above threshold" and the metric comes from the rule's SensorMetric instead.
    public record ThresholdConditionConfig(double Threshold, double Hysteresis);

    /// On for IntervalLength seconds out of every Interval-second period, grid-aligned to epoch - see AgrumyFirmware's computeIntervalState.
    public record IntervalConditionConfig(int Interval, int IntervalLength);

    /// One wall-clock window - multiple windows for the same function are multiple Schedule rules, OR'd together. DaysOfWeek: 7-bit mask (bit0=Sunday). Start/Duration: seconds since local midnight, must not cross midnight.
    public record ScheduleConditionConfig(int DaysOfWeek, int Start, int Duration);

    /// On from (today's sunrise + SunriseOffsetMinutes) to (today's sunset + SunsetOffsetMinutes) at ServerConfig.WeatherLocationLat/Lon, on the days in DaysOfWeek (same 7-bit mask as ScheduleConditionConfig) - negative offsets extend the window earlier, positive later, so e.g. (-30, 60) supplements natural daylight by 30 minutes at dawn and 60 at dusk.
    public record AstronomicalConditionConfig(int DaysOfWeek, int SunriseOffsetMinutes, int SunsetOffsetMinutes);

    /// True while ReferencedRuleId's own Conditions fold is true - only valid inside a Notification-action rule, referencing another Notification-action rule (same tenant, any zone/unit - cross-zone/cross-unit chaining is allowed).
    public record RuleTriggeredConditionConfig(int ReferencedRuleId);

    /// Per-sensor-type average from each device's LATEST reading only, not a historical average (which would skew by poll frequency); null means nothing in scope has reported that type.
    public class SensorAverages
    {
        public double? Temperature { get; set; }
        public double? SoilTemperature { get; set; }
        public double? Humidity { get; set; }
        /// Derived from Temperature+Humidity (api.Utils.VpdCalculator) - null whenever either is, never computed from a stale pairing.
        public double? Vpd { get; set; }
        public double? Moisture { get; set; }
        public double? Light { get; set; }
        public double? Co2 { get; set; }
        public double? Tvoc { get; set; }
        public double? Barometer { get; set; }
        public double? LiquidPH { get; set; }
        public double? RainLevel { get; set; }
        public double? WaterLevel { get; set; }
        public double? Wind { get; set; }
    }

    /// Traffic-light health for a Unit/Zone cube; Red beats Orange beats Green, so one offline device reddens the whole cube.
    public enum ZoneStatus
    {
        Green = 0,
        Orange = 1,
        Red = 2,
    }

    /// Last-24h hourly average per sensor type for the cube's sparklines - index 0 is the oldest bucket (24h ago), index 23 is the current (possibly partial) hour; a null bucket renders as a gap, not zero.
    public class SensorTrend
    {
        public const int HourBuckets = 24;

        public double?[] Temperature { get; set; } = new double?[HourBuckets];
        public double?[] SoilTemperature { get; set; } = new double?[HourBuckets];
        public double?[] Humidity { get; set; } = new double?[HourBuckets];
        public double?[] Vpd { get; set; } = new double?[HourBuckets];
        public double?[] Moisture { get; set; } = new double?[HourBuckets];
        public double?[] Light { get; set; } = new double?[HourBuckets];
        public double?[] Co2 { get; set; } = new double?[HourBuckets];
        public double?[] Tvoc { get; set; } = new double?[HourBuckets];
        public double?[] Barometer { get; set; } = new double?[HourBuckets];
        public double?[] LiquidPH { get; set; } = new double?[HourBuckets];
        public double?[] RainLevel { get; set; } = new double?[HourBuckets];
        public double?[] WaterLevel { get; set; } = new double?[HourBuckets];
        public double?[] Wind { get; set; } = new double?[HourBuckets];
    }

    /// One non-critical problem alert behind a Unit/Zone's Orange status - shown in _ZoneStatusBadge's dropdown, dismissable via DeviceApiController.DeviceEventAcknowledge.
    public class UnitZoneProblemAlert
    {
        public int IDEventDevice { get; set; }
        public int DeviceID { get; set; }
        public string? DeviceName { get; set; }
        public string? EventType { get; set; }
        public DateTime? Date { get; set; }
        public string? Message { get; set; }
    }

    /// One Unit cube on the top-level dashboard - name plus a roll-up over every sensor in every zone of this unit.
    public class DeviceUnitDashboard
    {
        public int IDDeviceUnit { get; set; }
        public string? DeviceUnitName { get; set; }
        public int ZoneCount { get; set; }
        public int DeviceCount { get; set; }
        public SensorAverages Averages { get; set; } = new();
        public ZoneStatus Status { get; set; }
        public SensorTrend Trend { get; set; } = new();
        public IList<UnitZoneProblemAlert> ProblemAlerts { get; set; } = new List<UnitZoneProblemAlert>();
    }

    /// One Zone cube inside a Unit's drill-down, same shape as DeviceUnitDashboard narrowed to this zone; Devices is populated only by the single-zone detail view, left empty on the zone-list view.
    public class DeviceUnitZoneDashboard
    {
        public int IDDeviceUnitZone { get; set; }
        public int IDDeviceUnit { get; set; }
        public string? DeviceUnitZoneName { get; set; }
        public int DeviceCount { get; set; }
        public SensorAverages Averages { get; set; } = new();
        public IList<Device> Devices { get; set; } = new List<Device>();
        public ZoneStatus Status { get; set; }
        public SensorTrend Trend { get; set; } = new();
        public IList<UnitZoneProblemAlert> ProblemAlerts { get; set; } = new List<UnitZoneProblemAlert>();
    }

    /// Body of the Add Controller/Add Sensor action - assigns an unassigned device to a zone; the zone's own DeviceUnitID resolves the Unit, so no separate unit id is needed.
    public class DeviceZoneAssignment
    {
        public int IDDevice { get; set; }
        public int IDDeviceUnitZone { get; set; }
    }
}
