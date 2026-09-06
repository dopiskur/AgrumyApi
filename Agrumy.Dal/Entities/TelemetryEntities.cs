namespace api.Dal.Entities
{
    public class SensorDataRow
    {
        public int IDSensorData { get; set; }
        public int TenantID { get; set; }
        public int DeviceID { get; set; }
        public int? DeviceUnitID { get; set; }
        public int? DeviceUnitZoneID { get; set; }
        public int? Battery { get; set; }
        public double? Temperature { get; set; }
        public double? SoilTemperature { get; set; }
        public double? Humidity { get; set; }
        public int? Moisture { get; set; }
        public int? Light { get; set; }
        public int? Co2 { get; set; }
        public int? Tvoc { get; set; }
        public double? Barometer { get; set; }
        public double? LiquidPH { get; set; }
        public int? RainLevel { get; set; }
        public int? WaterLevel { get; set; }
        public int? Wind { get; set; }
        public DateTime? DateCreated { get; set; }
    }

    /// Current on/off state per (DeviceID, RelayFunction) - one row per pair, upserted on every real state change, not an append-only log. Same table/logic for a real device (physical relay) and a simulated one (calculated), the difference is only where the IsOn decision comes from.
    public class ControllerDataRow
    {
        public int IDControllerData { get; set; }
        public int DeviceID { get; set; }
        public int TenantID { get; set; }
        public int RelayFunction { get; set; }
        public bool IsOn { get; set; }
        public DateTime? DateChanged { get; set; }
    }

    public class SensorDataReportRow
    {
        public int IDSensorDataReport { get; set; }
        public int? DeviceID { get; set; }
        public string? ReportName { get; set; }
        public DateTime? DateGenerated { get; set; }
        public string? SensorData { get; set; }
    }

    /// Catalog of api.Models.DeviceEventType values, seeded 1:1 from that enum - backs EventDeviceRow.EventID and EventServiceRow.EventID so a future event type has one source of truth instead of a magic-number agreement.
    public class EventTypeRow
    {
        public int IDEventType { get; set; }
        public string EventTypeName { get; set; } = "";
    }

    public class EventDeviceRow
    {
        public int IDEventDevice { get; set; }
        public int DeviceID { get; set; }
        public int TenantID { get; set; } // Stamped from the authenticated device's identity at push time, never from a client-supplied value.
        public int EventID { get; set; }
        public DateTime? Date { get; set; }
        public string? Message { get; set; }
        public DateTime? AcknowledgedAt { get; set; } // Set once an admin dismisses this alert, stopping it counting toward Unit/Zone Orange status even inside the expiry window.
    }

    public class EventServiceRow
    {
        public int IDEventService { get; set; }
        public int ServiceID { get; set; } // FKs to DeviceTypeServiceRow, the existing HTTP/HTTPS/MQTT catalog - this table has no live writer yet, but the naming already matches that catalog.
        public int EventID { get; set; }
        public DateTime? Date { get; set; }
        public string? Message { get; set; }
    }
}
