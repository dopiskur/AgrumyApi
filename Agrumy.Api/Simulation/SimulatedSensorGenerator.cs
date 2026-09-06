using api.Models;

namespace api.Simulation
{
    /// A plausible, gently-drifting synthetic reading per tick - not physically modeled, just a bounded random walk within api.Models.SimulationMetricRange so values stay realistic enough to exercise threshold/interval/schedule rules meaningfully. Registered as a singleton (not scoped) so per-device history survives across PeriodicBackgroundService's per-tick DI scopes.
    public class SimulatedSensorGenerator
    {
        private readonly Dictionary<int, SimulatedReading> lastByDevice = new();

        public SimulatedReading Next(int deviceId)
        {
            SimulatedReading last = lastByDevice.TryGetValue(deviceId, out var existing) ? existing : Seed();
            var next = new SimulatedReading
            {
                Temperature = Walk(last.Temperature, SimulationMetricRange.Temperature, 0.3),
                SoilTemperature = Walk(last.SoilTemperature, SimulationMetricRange.SoilTemperature, 0.2),
                Humidity = Walk(last.Humidity, SimulationMetricRange.Humidity, 1.5),
                Battery = (int)Walk(last.Battery, SimulationMetricRange.Battery, 0.05),
                Moisture = (int)Walk(last.Moisture, SimulationMetricRange.Moisture, 1.0),
                Light = (int)Walk(last.Light, SimulationMetricRange.Light, 500),
                Co2 = (int)Walk(last.Co2, SimulationMetricRange.Co2, 15),
                Tvoc = (int)Walk(last.Tvoc, SimulationMetricRange.Tvoc, 200),
                Barometer = Walk(last.Barometer, SimulationMetricRange.Barometer, 50),
                LiquidPH = Walk(last.LiquidPH, SimulationMetricRange.LiquidPH, 0.05),
                RainLevel = (int)Walk(last.RainLevel, SimulationMetricRange.RainLevel, 2),
                WaterLevel = (int)Walk(last.WaterLevel, SimulationMetricRange.WaterLevel, 1.0),
                Wind = (int)Walk(last.Wind, SimulationMetricRange.Wind, 3),
            };
            lastByDevice[deviceId] = next;
            return next;
        }

        /// Called when a virtual device is deleted - not strictly necessary (a re-registered device with the same id is vanishingly unlikely), but avoids an unbounded dictionary over a long-running server's lifetime.
        public void Forget(int deviceId) => lastByDevice.Remove(deviceId);

        private static double Walk(double current, (double Min, double Max) range, double maxStep)
        {
            double next = current + (Random.Shared.NextDouble() * 2 - 1) * maxStep;
            return Math.Clamp(next, range.Min, range.Max);
        }

        private static SimulatedReading Seed() => new()
        {
            Temperature = 22, SoilTemperature = 20, Humidity = 55, Battery = 90, Moisture = 40,
            Light = 5000, Co2 = 600, Tvoc = 200, Barometer = 101325, LiquidPH = 6.5,
            RainLevel = 0, WaterLevel = 50, Wind = 5,
        };
    }
}
