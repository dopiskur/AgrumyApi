using api.Models;

namespace api.ViewModels
{
    public class ReportingRow
    {
        public required SensorDataReport Report { get; set; }
        public required string DeviceName { get; set; }
    }
}
