namespace api.LoRa
{
    /// Config re-poll interval scaled to LoRa spreading factor so a weak-signal device doesn't blow EU868's ~1% duty-cycle budget; SF7/9/12 anchors are confirmed, SF8/10/11 are interpolated and unverified against real hardware.
    public static class LoRaInterval
    {
        private static readonly (int Sf, double Seconds)[] Anchors =
        [
            (7, 30),
            (9, 120),
            (12, 300),
        ];

        /// Seconds between config-poll retries for spreading factor 7-12; out-of-range values clamp to the nearest anchor.
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

                // Log-linear, not linear - airtime/duty-cycle cost grows roughly exponentially with SF.
                double t = (double)(clamped - loSf) / (hiSf - loSf);
                double logSeconds = Math.Log(loSeconds) + t * (Math.Log(hiSeconds) - Math.Log(loSeconds));
                return TimeSpan.FromSeconds(Math.Exp(logSeconds));
            }

            return TimeSpan.FromSeconds(Anchors[^1].Seconds);
        }
    }
}
