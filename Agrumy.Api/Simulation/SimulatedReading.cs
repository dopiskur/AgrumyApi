namespace api.Simulation
{
    /// One tick's full synthetic sensor snapshot for a virtual device - same field set as the real POST /api/SensorData wire shape, produced fresh each tick by SimulatedSensorGenerator.
    public class SimulatedReading
    {
        public double Temperature { get; set; }
        public double SoilTemperature { get; set; }
        public double Humidity { get; set; }
        public int Battery { get; set; }
        public int Moisture { get; set; }
        public int Light { get; set; }
        public int Co2 { get; set; }
        public int Tvoc { get; set; }
        public double Barometer { get; set; }
        public double LiquidPH { get; set; }
        public int RainLevel { get; set; }
        public int WaterLevel { get; set; }
        public int Wind { get; set; }
    }
}
