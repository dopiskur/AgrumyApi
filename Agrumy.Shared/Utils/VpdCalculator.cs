namespace api.Utils
{
    /// <summary>Air VPD (kPa) via the Tetens formula from Temperature (°C) + Humidity (%RH); null if either is missing.</summary>
    public static class VpdCalculator
    {
        public static double? Compute(double? temperatureC, double? humidityPercent)
        {
            if (temperatureC is not double t || humidityPercent is not double rh)
            {
                return null;
            }
            double saturationVaporPressureKPa = 0.6108 * Math.Exp(17.27 * t / (t + 237.3));
            return saturationVaporPressureKPa * (1 - rh / 100.0);
        }
    }
}
