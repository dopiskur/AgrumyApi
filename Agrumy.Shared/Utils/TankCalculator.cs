namespace api.Utils
{
    /// Linear interpolation between a zone's two raw WaterLevel calibration points (roadmap #234) - null unless a zone has set capacity and both calibration points.
    public static class TankCalculator
    {
        public static (double? Percent, double? VolumeLiters) Compute(double? rawWaterLevel, int? rawEmpty, int? rawFull, double? capacityLiters)
        {
            if (rawWaterLevel is not double raw || rawEmpty is not int empty || rawFull is not int full || capacityLiters is not double capacity || empty == full)
            {
                return (null, null);
            }
            double fraction = Math.Clamp((raw - empty) / (double)(full - empty), 0.0, 1.0);
            return (fraction * 100.0, fraction * capacity);
        }
    }
}
