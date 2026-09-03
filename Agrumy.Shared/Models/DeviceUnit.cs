using Microsoft.AspNetCore.Mvc;

namespace api.Models
{
    /// <summary>Roadmap #82: a physical/logical space (e.g. a greenhouse) an admin creates,
    /// containing one or more <see cref="DeviceUnitZone"/>. TenantID null only ever means the
    /// shared IDDeviceUnit=0 "Default" sentinel row every not-yet-zoned device points at - real,
    /// admin-created Units always carry the creator's tenant.</summary>
    public class DeviceUnit
    {
        [HiddenInput(DisplayValue = true)]
        public int? IDDeviceUnit { get; set; }
        public int? TenantID { get; set; }
        public string? DeviceUnitName { get; set; }
    }

    /// <summary>Roadmap #81/#82: a growing zone within one <see cref="DeviceUnit"/> - "one zone =
    /// one controller" (at most; a zone may be sensor-only, see #82 rule (a)). TenantID mirrors
    /// DeviceUnit.TenantID (denormalized so a zone-scoped query needs no join back to Unit just to
    /// check tenant ownership).</summary>
    public class DeviceUnitZone
    {
        [HiddenInput(DisplayValue = true)]
        public int? IDDeviceUnitZone { get; set; }
        public int? TenantID { get; set; }
        public int DeviceUnitID { get; set; }
        public string? DeviceUnitZoneName { get; set; }

        // Roadmap #21/#36: WaterPump-only device-side hard safety limits, moved here from the
        // per-device DeviceConfigController - NOT a Rule (see DeviceUnitZoneRule below), an override
        // ceiling applied by the device AFTER any rule has already decided WaterPump should be on.
        // Seeded from AgrumySettings.WaterPumpMaxRunSeconds/CooldownSeconds when a zone is created,
        // same pattern the pre-#21 per-device seeding used.
        public int? WaterPumpMaxRunSeconds { get; set; }
        public int? WaterPumpCooldownSeconds { get; set; }
    }

    /// <summary>Roadmap #21: which relay function a <see cref="DeviceUnitZoneRule"/> targets - same
    /// numeric convention as the pre-#21 DeviceScheduleSlot.RelayFunction and the deviceTypeRelay
    /// seed rows (1=Ventilation, 2=Light, 3=Heating, 4=WaterPump). Kept as a plain int on the DTO/
    /// entity (matching DeviceScheduleSlot's own RelayFunction) rather than this enum, to avoid a
    /// JsonStringEnumConverter dependency on a wire field the firmware also has to parse as a number.</summary>
    public enum RelayFunction
    {
        Ventilation = 1,
        Light = 2,
        Heating = 3,
        WaterPump = 4,
    }

    /// <summary>Roadmap #21: which kind of condition a <see cref="DeviceUnitZoneRule"/> evaluates -
    /// see ThresholdConditionConfig/IntervalConditionConfig/ScheduleConditionConfig below for each
    /// type's ConditionConfig shape. Composite (AND/OR across different condition types) is
    /// deliberately out of scope - see the roadmap #21 note.</summary>
    public enum ConditionType
    {
        Threshold = 1,
        Interval = 2,
        Schedule = 3,
    }

    /// <summary>Roadmap #21: a plain System.Text.Json.Nodes.JsonNode has no C# property metadata to
    /// re-key against - once built, its keys are frozen exactly as whatever options constructed it,
    /// and an OUTER JsonSerializer.Serialize(..., campelCaseOptions) call does NOT walk into an
    /// already-materialized JsonNode to re-apply that policy (confirmed by a contract test that
    /// caught this: building a ConditionConfig via the options-less SerializeToNode overload leaked
    /// PascalCase - "Threshold" not "threshold" - straight onto the wire). Every place that builds OR
    /// reads a DeviceUnitZoneRule.ConditionConfig (Web's Add-Rule form, DeviceUnitApiController's
    /// RuleConditionConfigError validation, tests) must use these exact options, not the parameterless
    /// JsonSerializer overloads, or camelCase drifts to PascalCase (or a Deserialize&lt;T&gt; call
    /// silently fails to bind - PropertyNameCaseInsensitive is off by default) the moment the JSON
    /// crosses this boundary.</summary>
    public static class ConditionConfigJson
    {
        public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);
    }

    /// <summary>Roadmap #21: one automation rule, living on the ZONE (not the device) so a
    /// controller-replacement device assigned to the same zone immediately runs the zone's existing
    /// rules with no extra step (closes #137 as a side effect). A zone may hold several rules for the
    /// SAME RelayFunction (user decision, 2026-09-04) - OR semantics, any rule that evaluates "on"
    /// wins; there is no AND/composite combination. ConditionConfig is a JSON blob (user decision)
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

    /// <summary>Roadmap #21: threshold+hysteresis for whichever single metric/direction is already
    /// implicit in the rule's RelayFunction (Ventilation=humidity/above, Light=light/below,
    /// Heating=temperature/below, WaterPump=waterLevel/below - see AgrumyFirmware's
    /// ActuatorController::evaluateRule, unchanged from the pre-#21 thresholdRelayFunction switch).
    /// Deliberately ONE threshold value, not a low/high pair: the pre-#21 flat model carried both,
    /// but the actual dispatch switch only ever read one bound per function - the other was always
    /// dead wire data, confirmed in code before this shape was chosen.</summary>
    public record ThresholdConditionConfig(double Threshold, double Hysteresis);

    /// <summary>Roadmap #21: on for IntervalLength seconds out of every Interval-second period,
    /// grid-aligned to epoch (see AgrumyFirmware's computeIntervalState) - same two numbers the
    /// pre-#21 per-function *Interval/*IntervalLength fields carried.</summary>
    public record IntervalConditionConfig(int Interval, int IntervalLength);

    /// <summary>Roadmap #21: ONE wall-clock window (unlike the pre-#21 DeviceScheduleSlot, this is
    /// not a list - multiple windows for the same function are now multiple Schedule-type rules,
    /// OR'd together like any other pair of rules for that function, see DeviceUnitZoneRule's own
    /// remarks). DaysOfWeek: 7-bit mask, bit 0 = Sunday .. bit 6 = Saturday. Start: seconds since
    /// local midnight, 0-86399. Duration: seconds; Start+Duration must not exceed 86400 (no crossing
    /// local midnight) - same validation DeviceScheduleSlot already required.</summary>
    public record ScheduleConditionConfig(int DaysOfWeek, int Start, int Duration);

    /// <summary>Roadmap #81: per-sensor-type average, each type kept independent (never mixed,
    /// never averaged together with another type) and computed only from the latest reading of
    /// each device in scope - not a historical average, which would skew by poll frequency.
    /// Calibration differences between two sensors of the same type are deliberately ignored (user
    /// confirmed design). A null field means no device in scope has ever reported that type.</summary>
    public class SensorAverages
    {
        public double? Temperature { get; set; }
        public double? SoilTemperature { get; set; }
        public double? Humidity { get; set; }
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

    /// <summary>Roadmap #116 rule (4): traffic-light health summary for a Unit/Zone cube - pure
    /// aggregation of data that already exists, no new tracking mechanism. Priority Red > Orange >
    /// Green (a single offline device makes the whole cube Red even if nothing else is wrong).</summary>
    public enum ZoneStatus
    {
        Green = 0,
        Orange = 1,
        Red = 2,
    }

    /// <summary>Roadmap #116 rule (3): last-24h hourly average per sensor type, for the cube's mini
    /// sparklines - same field set/semantics as SensorAverages, but each is 24 buckets instead of
    /// one number. Index 0 = the bucket ending 24h ago (oldest), index 23 = the bucket ending now
    /// (current hour, may be partial). A null bucket means no device in scope reported that type in
    /// that hour - rendered as a gap in the sparkline, not a zero.</summary>
    public class SensorTrend
    {
        public const int HourBuckets = 24;

        public double?[] Temperature { get; set; } = new double?[HourBuckets];
        public double?[] SoilTemperature { get; set; } = new double?[HourBuckets];
        public double?[] Humidity { get; set; } = new double?[HourBuckets];
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

    /// <summary>Roadmap #81: one Unit cube on the top-level dashboard - name plus a roll-up over
    /// every sensor in every zone of this unit.</summary>
    public class DeviceUnitDashboard
    {
        public int IDDeviceUnit { get; set; }
        public string? DeviceUnitName { get; set; }
        public int ZoneCount { get; set; }
        public int DeviceCount { get; set; }
        public SensorAverages Averages { get; set; } = new();
        public ZoneStatus Status { get; set; } // roadmap #116 rule (4)
        public SensorTrend Trend { get; set; } = new(); // roadmap #116 rule (3)
    }

    /// <summary>Roadmap #81: one Zone cube inside a Unit's drill-down - same shape as
    /// DeviceUnitDashboard, narrowed to this zone's own devices. <see cref="Devices"/> is populated
    /// only by the single-zone detail view (DeviceUnitDashboardZoneGetAsync), left empty on the
    /// zone-list-within-a-unit view where only the roll-up numbers are shown.</summary>
    public class DeviceUnitZoneDashboard
    {
        public int IDDeviceUnitZone { get; set; }
        public int IDDeviceUnit { get; set; }
        public string? DeviceUnitZoneName { get; set; }
        public int DeviceCount { get; set; }
        public SensorAverages Averages { get; set; } = new();
        public IList<Device> Devices { get; set; } = new List<Device>();
        public ZoneStatus Status { get; set; } // roadmap #116 rule (4)
        public SensorTrend Trend { get; set; } = new(); // roadmap #116 rule (3)
    }

    /// <summary>Roadmap #82: body of the Add Controller/Add Sensor action - assigns one
    /// already-unassigned device to one zone. The zone's own DeviceUnitID resolves the Unit, so the
    /// caller does not also pass a unit id (avoids a payload that could disagree with the zone).</summary>
    public class DeviceZoneAssignment
    {
        public int IDDevice { get; set; }
        public int IDDeviceUnitZone { get; set; }
    }
}
