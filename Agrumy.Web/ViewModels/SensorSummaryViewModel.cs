using api.Models;

namespace api.ViewModels
{
    /// <summary>Roadmap #116 rule (3): pairs a cube's current-value averages with its 24h trend,
    /// so _SensorAverages.cshtml can render a mini sparkline next to each badge.</summary>
    public class SensorSummaryViewModel
    {
        public required SensorAverages Averages { get; init; }
        public SensorTrend? Trend { get; init; }
    }
}
