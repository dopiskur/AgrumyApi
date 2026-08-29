namespace api.Dal.Entities
{
    public class SensorDataRow
    {
        public int IDSensorData { get; set; }
        public int TenantID { get; set; }
        public int DeviceID { get; set; }
        public int DeviceUnitID { get; set; }
        public int DeviceUnitZoneID { get; set; }
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

    public class SensorDataReportRow
    {
        public int IDSensorDataReport { get; set; }
        public int? DeviceID { get; set; }
        public string? ReportName { get; set; }
        public DateTime? DateGenerated { get; set; }
        public string? SensorData { get; set; }
    }

    public class EventDeviceRow
    {
        public int IDEventDevice { get; set; }
        public int DeviceID { get; set; }
        public int EventID { get; set; }
        public DateTime? Date { get; set; }
        public string? Message { get; set; }
    }

    public class EventServiceRow
    {
        public int IDEventService { get; set; }
        public int ServiceID { get; set; }
        public int EventID { get; set; }
        public DateTime? Date { get; set; }
        public string? Message { get; set; }
    }
}
