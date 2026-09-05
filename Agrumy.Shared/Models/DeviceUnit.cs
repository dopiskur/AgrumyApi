using Microsoft.AspNetCore.Mvc;

namespace api.Models
{
    /// <summary>A physical/logical space (e.g. a greenhouse) an admin creates, containing one or
    /// more <see cref="DeviceUnitZone"/>. TenantID null only ever means the shared IDDeviceUnit=0
    /// "Default" sentinel row every not-yet-zoned device points at - real, admin-created Units
    /// always carry the creator's tenant.</summary>
    public class DeviceUnit
    {
        [HiddenInput(DisplayValue = true)]
        public int? IDDeviceUnit { get; set; }
        public int? TenantID { get; set; }
        public string? DeviceUnitName { get; set; }
    }

    /// <summary>A growing zone within one <see cref="DeviceUnit"/> - "one zone = one controller" at
    /// most; a zone may be sensor-only. TenantID mirrors DeviceUnit.TenantID (denormalized so a
    /// zone-scoped query needs no join back to Unit just to check tenant ownership).</summary>
    public class DeviceUnitZone
    {
        [HiddenInput(DisplayValue = true)]
        public int? IDDeviceUnitZone { get; set; }
        public int? TenantID { get; set; }
        public int DeviceUnitID { get; set; }
        public string? DeviceUnitZoneName { get; set; }

        // WaterPump-only device-side hard safety limits - NOT a Rule (see DeviceUnitZoneRule
        // below), an override ceiling applied by the device AFTER any rule has already decided
        // WaterPump should be on. Seeded from AgrumySettings.WaterPumpMaxRunSeconds/CooldownSeconds when created.
        public int? WaterPumpMaxRunSeconds { get; set; }
        public int? WaterPumpCooldownSeconds { get; set; }

        // Per-zone opt-in - not every zone waters something rain makes redundant, so this is a
        // deliberate admin choice, not a global switch. Combined server-side with
        // ServerConfig.WeatherRainPredicted into DeviceConfigController.SkipWaterPumpForRain.
        public bool SkipWaterPumpWhenRainPredicted { get; set; }
    }

    /// <summary>Which relay function a <see cref="DeviceUnitZoneRule"/> targets - same numeric
    /// convention as DeviceScheduleSlot.RelayFunction and the deviceTypeRelay seed rows
    /// (1=Ventilation, 2=Light, 3=Heating, 4=WaterPump). Kept as a plain int on the DTO/entity
    /// rather than this enum, to avoid a JsonStringEnumConverter dependency on a wire field the
    /// firmware also has to parse as a number.</summary>
    public enum RelayFunction
    {
        Ventilation = 1,
        Light = 2,
        Heating = 3,
        WaterPump = 4,
    }

    /// <summary>Which kind of condition a <see cref="DeviceUnitZoneRule"/> evaluates - see
    /// ThresholdConditionConfig/IntervalConditionConfig/ScheduleConditionConfig below for each
    /// type's ConditionConfig shape. Composite (AND/OR across different condition types) is
    /// deliberately out of scope.</summary>
    public enum ConditionType
    {
        Threshold = 1,
        Interval = 2,
        Schedule = 3,
    }

    /// <summary>A plain System.Text.Json.Nodes.JsonNode has no C# property metadata to re-key
    /// against - once built, its keys are frozen exactly as whatever options constructed it, and an
    /// OUTER JsonSerializer.Serialize(..., camelCaseOptions) call does NOT walk into an
    /// already-materialized JsonNode to re-apply that policy. Every place that builds or reads a
    /// DeviceUnitZoneRule.ConditionConfig must use these exact options, not the parameterless
    /// JsonSerializer overloads, or camelCase drifts to PascalCase on the wire.</summary>
    public static class ConditionConfigJson
    {
        public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);
    }

    /// <summary>One automation rule, living on the ZONE (not the device) so a controller-replacement
    /// device assigned to the same zone immediately runs the zone's existing rules with no extra
    /// step. A zone may hold several rules for the SAME RelayFunction - OR semantics, any rule that
    /// evaluates "on" wins; there is no AND/composite combination. ConditionConfig is a JSON blob
    /// whose shape depends on ConditionType - see the three ConditionConfig-suffixed records below,
    /// which are ONLY (de)serialization helpers for that JSON, not additional wire fields.</summary>
    public class DeviceUnitZoneRule
    {
        [HiddenInput(DisplayValue = true)]
        public int? IDDeviceUnitZoneRule { get; set; }
        public int DeviceUnitZoneID { get; set; }
        public RelayFunction RelayFunction { get; set; }
        public ConditionType ConditionType { get; set; }
        public System.Text.Json.Nodes.JsonNode? ConditionConfig { get; set; }
    }

    /// <summary>Threshold+hysteresis for whichever single metric/direction is already implicit in
    /// the rule's RelayFunction (Ventilation=humidity/above, Light=light/below,
    /// Heating=temperature/below, WaterPump=waterLevel/below - see AgrumyFirmware's
    /// ActuatorController::evaluateRule). Deliberately ONE threshold value, not a low/high pair -
    /// the dispatch switch only ever reads one bound per function.</summary>
    public record ThresholdConditionConfig(double Threshold, double Hysteresis);

    /// <summary>On for IntervalLength seconds out of every Interval-second period, grid-aligned to
    /// epoch - see AgrumyFirmware's computeIntervalState.</summary>
    public record IntervalConditionConfig(int Interval, int IntervalLength);

    /// <summary>ONE wall-clock window - multiple windows for the same function are multiple
    /// Schedule-type rules, OR'd together like any other pair of rules for that function.
    /// DaysOfWeek: 7-bit mask, bit 0 = Sunday .. bit 6 = Saturday. Start: seconds since local
    /// midnight, 0-86399. Duration: seconds; Start+Duration must not exceed 86400 (no crossing
    /// local midnight).</summary>
    public record ScheduleConditionConfig(int DaysOfWeek, int Start, int Duration);

    /// <summary>Per-sensor-type average, each type kept independent and computed only from the
    /// latest reading of each device in scope - not a historical average, which would skew by poll
    /// frequency. A null field means no device in scope has ever reported that type.</summary>
    public class SensorAverages
    {
        public double? Temperature { get; set; }
        public double? SoilTemperature { get; set; }
        public double? Humidity { get; set; }
        /// <summary>Derived from Temperature+Humidity (api.Utils.VpdCalculator) - null whenever either is, never computed from a stale pairing.</summary>
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

    /// <summary>Traffic-light health summary for a Unit/Zone cube. Priority Red > Orange > Green
    /// (a single offline device makes the whole cube Red even if nothing else is wrong).</summary>
    public enum ZoneStatus
    {
        Green = 0,
        Orange = 1,
        Red = 2,
    }

    /// <summary>Last-24h hourly average per sensor type, for the cube's mini sparklines - same
    /// field set/semantics as SensorAverages, but each is 24 buckets instead of one number. Index 0
    /// = the bucket ending 24h ago (oldest), index 23 = the bucket ending now (current hour, may be
    /// partial). A null bucket is rendered as a gap in the sparkline, not a zero.</summary>
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

    /// <summary>One non-critical problem alert in scope for a Unit/Zone's Orange status - shown in
    /// _ZoneStatusBadge's dropdown, dismissable via DeviceApiController.DeviceEventAcknowledge.</summary>
    public class UnitZoneProblemAlert
    {
        public int IDEventDevice { get; set; }
        public int DeviceID { get; set; }
        public string? DeviceName { get; set; }
        public string? EventType { get; set; }
        public DateTime? Date { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>One Unit cube on the top-level dashboard - name plus a roll-up over every sensor in
    /// every zone of this unit.</summary>
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

    /// <summary>One Zone cube inside a Unit's drill-down - same shape as DeviceUnitDashboard,
    /// narrowed to this zone's own devices. <see cref="Devices"/> is populated only by the
    /// single-zone detail view, left empty on the zone-list-within-a-unit view where only the
    /// roll-up numbers are shown.</summary>
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

    /// <summary>Body of the Add Controller/Add Sensor action - assigns one already-unassigned
    /// device to one zone. The zone's own DeviceUnitID resolves the Unit, so the caller does not
    /// also pass a unit id.</summary>
    public class DeviceZoneAssignment
    {
        public int IDDevice { get; set; }
        public int IDDeviceUnitZone { get; set; }
    }
}
