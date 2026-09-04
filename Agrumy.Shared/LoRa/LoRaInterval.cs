namespace api.LoRa
{
    /// <summary>How often a battery/mains LoRa end-device should re-poll for pending
    /// config after a RelayBatchEntryResult comes back empty, scaled to spreading factor so a
    /// weak-signal device (SF12, long airtime) doesn't blow EU868's ~1% duty-cycle budget the way a
    /// fixed poll interval would. Anchor points (SF7=30s, SF9=2min, SF12=5min) are confirmed; the
    /// SF8/10/11 values are log-linear interpolation between them, not independently verified -
    /// flag for a real duty-cycle measurement once LoRa firmware exists to test against.</summary>
    public static class LoRaInterval
    {
        private static readonly (int Sf, double Seconds)[] Anchors =
        [
            (7, 30),
            (9, 120),
            (12, 300),
        ];

        /// <summary>Seconds between config-poll retries for the given spreading factor (7-12).
        /// Values outside that range clamp to the nearest anchor rather than extrapolating.</summary>
        public static TimeSpan ForSpreadingFactor(int sf)
        {
            int clamped = Math.Clamp(sf, Anchors[0].Sf, Anchors[^1].Sf);

            for (int i = 0; i < Anchors.Length - 1; i++)
            {
                var (loSf, loSeconds) = Anchors[i];
                var (hiSf, hiSeconds) = Anchors[i + 1];
                if (clamped < loSf || clamped > hiSf)
                {
                    continue;
                }

                if (clamped == loSf) return TimeSpan.FromSeconds(loSeconds);
                if (clamped == hiSf) return TimeSpan.FromSeconds(hiSeconds);

                // Log-linear: airtime (and so duty-cycle cost) grows roughly exponentially with SF,
                // not linearly, so interpolating the log of the interval tracks that curve better
                // than a straight line between the two anchor seconds values would.
                double t = (double)(clamped - loSf) / (hiSf - loSf);
                double logSeconds = Math.Log(loSeconds) + t * (Math.Log(hiSeconds) - Math.Log(loSeconds));
                return TimeSpan.FromSeconds(Math.Exp(logSeconds));
            }

            return TimeSpan.FromSeconds(Anchors[^1].Seconds);
        }
    }
}
