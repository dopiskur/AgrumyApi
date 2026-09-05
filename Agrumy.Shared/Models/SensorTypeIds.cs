namespace api.Models
{
    /// Canonical deviceTypeSensor IDs - must match AgrumyFirmware's SensorController.h/.cpp SensorTypeIds:: constants exactly, renumbering desyncs the two independently-versioned repos.
    public static class SensorTypeIds
    {
        public const int Disabled = 0;
        public const int Dht11 = 1001;
        public const int Dht22 = 1002;
        public const int Bmp180 = 1003;
        public const int Bmp280 = 1004;
        public const int Bme280 = 1005;
        public const int Ccs811 = 1006;
        public const int Ds18B20 = 1007;
        public const int Bh1750 = 1008;
        public const int Max17048 = 1009;
        public const int AnalogVoltage = 2001;
        public const int AnalogMoisture = 2002;
        public const int AnalogWaterLevel = 2003;
    }
}
