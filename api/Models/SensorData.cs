using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace api.Models
{
    
    public class SensorData
    {
        // DEVICE 
        public int? TenantID { get; set; }
        public int? DeviceID { get; set; }
        public int? DeviceUnitID { get; set; }
        public int? DeviceUnitZoneID { get; set; }

        // SENSOR
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
        public double? Wind { get; set; }
        public DateTime DateCreated { get; set; }


    }

    public enum TimeRangeMDMY
    {
        Minute = 0,
        Day = 1,
        Month = 2,
        Year = 3
    }

    public class TimeRange
    {
        [Range(1, 30)]
        public int? Range { get; set; } = 1;
    }


    public class SensorDataReport
    {
        public int? IDSensorDataReport { get; set; }
        public int? DeviceID { get; set; }
        public string? ReportName { get; set; }
        public DateTime? DateGenerated { get; set; }

        public string? SensorData { get; set; }

    }


}
