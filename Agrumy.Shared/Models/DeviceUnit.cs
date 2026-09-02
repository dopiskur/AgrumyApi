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
    }

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
