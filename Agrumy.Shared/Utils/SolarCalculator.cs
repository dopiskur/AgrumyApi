namespace api.Utils
{
    /// NOAA solar-position formulas (the same ones behind NOAA's published sunrise/sunset calculator, ~1 minute accuracy) - used by api.Devices.AstronomicalRuleResolver to turn a lat/lon into today's sunrise/sunset. Longitude is positive East, matching ServerConfig.WeatherLocationLon/the OpenWeatherMap client already in this codebase.
    public static class SolarCalculator
    {
        /// Seconds since local midnight for sunrise/sunset on localDate, or null for either when the sun never rises/sets that day (polar day/night) at this latitude.
        public static (int? SunriseSeconds, int? SunsetSeconds) Compute(DateOnly localDate, double latitude, double longitude, int utcOffsetSeconds)
        {
            DateTime localNoon = localDate.ToDateTime(new TimeOnly(12, 0, 0));
            DateTime utcNoon = localNoon.AddSeconds(-utcOffsetSeconds);
            double julianDay = utcNoon.ToOADate() + 2415018.5;
            double t = (julianDay - 2451545.0) / 36525.0;

            double l0 = GeomMeanLongSunDeg(t);
            double m = GeomMeanAnomalySunDeg(t);
            double e = EccentricityEarthOrbit(t);
            double trueLong = l0 + SunEqOfCenterDeg(t, m);
            double omega = 125.04 - 1934.136 * t;
            double appLong = trueLong - 0.00569 - 0.00478 * Math.Sin(Deg2Rad(omega));
            double obliqCorr = MeanObliquityOfEclipticDeg(t) + 0.00256 * Math.Cos(Deg2Rad(omega));
            double declinationDeg = Rad2Deg(Math.Asin(Math.Sin(Deg2Rad(obliqCorr)) * Math.Sin(Deg2Rad(appLong))));
            double eqTimeMinutes = EquationOfTimeMinutes(t, obliqCorr, l0, e, m);

            double latRad = Deg2Rad(latitude);
            double decRad = Deg2Rad(declinationDeg);
            double haArg = Math.Cos(Deg2Rad(90.833)) / (Math.Cos(latRad) * Math.Cos(decRad)) - Math.Tan(latRad) * Math.Tan(decRad);
            if (haArg is < -1 or > 1)
            {
                return (null, null);
            }
            double hourAngleDeg = Rad2Deg(Math.Acos(haArg));

            double solarNoonUtcMinutes = 720 - 4 * longitude - eqTimeMinutes;
            int sunrise = ToLocalSecondsSinceMidnight(solarNoonUtcMinutes - 4 * hourAngleDeg, utcOffsetSeconds);
            int sunset = ToLocalSecondsSinceMidnight(solarNoonUtcMinutes + 4 * hourAngleDeg, utcOffsetSeconds);
            return (sunrise, sunset);
        }

        private static int ToLocalSecondsSinceMidnight(double utcMinutes, int utcOffsetSeconds)
        {
            double localSeconds = utcMinutes * 60 + utcOffsetSeconds;
            localSeconds %= 86400;
            if (localSeconds < 0)
            {
                localSeconds += 86400;
            }
            return (int)Math.Round(localSeconds);
        }

        private static double GeomMeanLongSunDeg(double t)
        {
            double l = 280.46646 + t * (36000.76983 + t * 0.0003032);
            l %= 360;
            return l < 0 ? l + 360 : l;
        }

        private static double GeomMeanAnomalySunDeg(double t) => 357.52911 + t * (35999.05029 - 0.0001537 * t);

        private static double EccentricityEarthOrbit(double t) => 0.016708634 - t * (0.000042037 + 0.0000001267 * t);

        private static double SunEqOfCenterDeg(double t, double mDeg)
        {
            double mRad = Deg2Rad(mDeg);
            return Math.Sin(mRad) * (1.914602 - t * (0.004817 + 0.000014 * t))
                 + Math.Sin(2 * mRad) * (0.019993 - 0.000101 * t)
                 + Math.Sin(3 * mRad) * 0.000289;
        }

        private static double MeanObliquityOfEclipticDeg(double t)
        {
            double seconds = 21.448 - t * (46.815 + t * (0.00059 - t * 0.001813));
            return 23.0 + (26.0 + seconds / 60.0) / 60.0;
        }

        private static double EquationOfTimeMinutes(double t, double obliqCorrDeg, double l0Deg, double e, double mDeg)
        {
            double y = Math.Tan(Deg2Rad(obliqCorrDeg) / 2.0);
            y *= y;
            double l0Rad = Deg2Rad(l0Deg);
            double mRad = Deg2Rad(mDeg);
            double etime = y * Math.Sin(2 * l0Rad)
                         - 2 * e * Math.Sin(mRad)
                         + 4 * e * y * Math.Sin(mRad) * Math.Cos(2 * l0Rad)
                         - 0.5 * y * y * Math.Sin(4 * l0Rad)
                         - 1.25 * e * e * Math.Sin(2 * mRad);
            return Rad2Deg(etime) * 4.0;
        }

        private static double Deg2Rad(double deg) => deg * Math.PI / 180.0;
        private static double Rad2Deg(double rad) => rad * 180.0 / Math.PI;
    }
}
