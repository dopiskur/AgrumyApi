using api.Models;

namespace api.ViewModels
{
    public class SensorSummaryViewModel
    {
        public required SensorAverages Averages { get; init; }
        public SensorTrend? Trend { get; init; }
    }
}
