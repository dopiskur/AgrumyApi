using api.Models;
using MySql.Data.MySqlClient;
using System.Data;
using System.Text.Json.Nodes;
using api.Dal.Interface;
using api.Schema;
using Dapper;


namespace api.Dal
{
    internal class SqlRepository : IRepository
    {

        private static string? sqlcon = Config.defaultSqlCon;

        // QUERY

        // STARTUP / HEALTH

        public async Task<bool> TestConnectionAsync()
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            return connection.State == System.Data.ConnectionState.Open;
        }

        public async Task EnsureSchemaAsync()
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();

            // The CREATE TABLE IF NOT EXISTS / CREATE OR REPLACE statements below are safe to
            // rerun on their own, but there's no reason to pay that cost - or toggle
            // FOREIGN_KEY_CHECKS off against a database that may have live traffic - on every
            // single startup once the schema is already there.
            if (await TableExistsAsync(connection, SchemaScripts.KeyTable))
            {
                return;
            }

            using (var fkOff = connection.CreateCommand())
            {
                fkOff.CommandText = "SET FOREIGN_KEY_CHECKS = 0";
                await fkOff.ExecuteNonQueryAsync();
            }

            foreach (string batch in SchemaScripts.AllObjects)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = batch;
                await cmd.ExecuteNonQueryAsync();
            }

            using (var fkOn = connection.CreateCommand())
            {
                fkOn.CommandText = "SET FOREIGN_KEY_CHECKS = 1";
                await fkOn.ExecuteNonQueryAsync();
            }
        }

        public DbFailureKind ClassifyException(Exception ex)
        {
            // MySql error numbers: 1146 ER_NO_SUCH_TABLE, 1051 ER_BAD_TABLE_ERROR, 1305 SP_DOES_NOT_EXIST
            if (ex is MySqlException mysqlEx)
            {
                switch (mysqlEx.Number)
                {
                    case 1146:
                    case 1051:
                    case 1305:
                        return DbFailureKind.SchemaMissing;
                }
            }

            if (ex.Message.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("Unknown table", StringComparison.OrdinalIgnoreCase))
            {
                return DbFailureKind.SchemaMissing;
            }

            return DbFailureKind.ConnectionFailure;
        }

        private static async Task<bool> TableExistsAsync(MySqlConnection connection, string tableName)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT COUNT(*) FROM information_schema.tables " +
                "WHERE table_schema = DATABASE() AND table_name = @tableName";
            cmd.Parameters.AddWithValue("@tableName", tableName);
            object? result = await cmd.ExecuteScalarAsync();
            return result != null && Convert.ToInt64(result) > 0;
        }

        // AUTHENTICATION

        public async Task<ServerConfig> ServerConfigGetAsync(int idServerConfig = 1)
        {
            using var connection = new MySqlConnection(sqlcon);
            var existing = await connection.QuerySingleOrDefaultAsync<ServerConfig>(
                "ServerConfigGet",
                new { IDServerConfig = idServerConfig },
                commandType: CommandType.StoredProcedure);

            if (existing != null)
            {
                return existing;
            }


            ServerConfig generatedConfig = new ServerConfig
            {
                // ovdje je potencijalni problem gdje admin nece moc do svog passworda
                // ResetAdminPass("newpass");
                IDServerConfig = idServerConfig,
                ServerConfigName = "DefaultGenerated" + idServerConfig.ToString(),
                ConfigKey = Guid.NewGuid().ToString(),
                PortHTTP = 80,
                PortHTTPS = 443
            };

            await ServerConfigAddAsync(generatedConfig);
            return generatedConfig;

            //throw new ArgumentException("Wrong id, no such server config");
        }

        private async Task ServerConfigAddAsync(ServerConfig serverConfig)
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = "ServerConfigAdd";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(nameof(ServerConfig.IDServerConfig), serverConfig.IDServerConfig);
            cmd.Parameters.AddWithValue(nameof(ServerConfig.ServerConfigName), serverConfig.ServerConfigName);
            cmd.Parameters.AddWithValue(nameof(ServerConfig.ConfigKey), serverConfig.ConfigKey);
            cmd.Parameters.AddWithValue(nameof(ServerConfig.PortHTTP), serverConfig.PortHTTP);
            cmd.Parameters.AddWithValue(nameof(ServerConfig.PortHTTPS), serverConfig.PortHTTPS);


            await cmd.ExecuteNonQueryAsync();
        }

        private async Task ServerConfigUpdateAsync(ServerConfig serverConfig)
        {
            using var connection = new MySqlConnection(sqlcon);

            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = "ServerConfigUpdate";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(nameof(ServerConfig.ServerConfigName), serverConfig.ServerConfigName);
            cmd.Parameters.AddWithValue(nameof(ServerConfig.PortHTTP), serverConfig.PortHTTP);
            cmd.Parameters.AddWithValue(nameof(ServerConfig.PortHTTPS), serverConfig.PortHTTPS);

            cmd.Parameters.AddWithValue(nameof(ServerConfig.IDServerConfig), serverConfig.IDServerConfig);
            await cmd.ExecuteNonQueryAsync();

            // CAN NOT CHANGE ConfigKey, that would distrupt passwords!
        }

        // USER
        public async Task UserAddAsync(User user, UserSecret userSecret)
        {

            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = "UserAdd";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(nameof(User.TenantID), user.TenantID);
            cmd.Parameters.AddWithValue(nameof(User.Email), user.Email);
            cmd.Parameters.AddWithValue(nameof(User.Username), user.Username);
            cmd.Parameters.AddWithValue(nameof(User.DevicePin), user.DevicePin);
            cmd.Parameters.AddWithValue(nameof(UserSecret.PwdHash), userSecret.PwdHash);
            cmd.Parameters.AddWithValue(nameof(UserSecret.PwdSalt), userSecret.PwdSalt);
            cmd.Parameters.AddWithValue(nameof(User.FirstName), user.FirstName);
            cmd.Parameters.AddWithValue(nameof(User.LastName), user.LastName);
            cmd.Parameters.AddWithValue(nameof(User.Phone), user.Phone);
            cmd.Parameters.AddWithValue(nameof(User.UserGroupID), user.UserGroupID);
            cmd.Parameters.AddWithValue(nameof(User.Enabled), user.Enabled);

            await cmd.ExecuteNonQueryAsync();

        }
        public async Task<bool> UserDeleteAsync(int? idUser)
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = "UserDelete";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(User.IDUser), idUser);

            long rows = (long)(await cmd.ExecuteScalarAsync())!;

            if (rows > 0) { return true; }
            return false;
        }
        // All users
        public async Task<IList<User>> UsersGetAsync(int? tenantID)
        {
            using var connection = new MySqlConnection(sqlcon);
            var rows = await connection.QueryAsync<User>(
                "UsersGet",
                new { TenantID = tenantID },
                commandType: CommandType.StoredProcedure);
            return rows.AsList();
        }
        // Single user
        public async Task<User> UserGetAsync(int? idUser, string? email, string? username)
        {
            using var connection = new MySqlConnection(sqlcon);
            var user = await connection.QuerySingleOrDefaultAsync<User>(
                "UserGet",
                new { IDUser = idUser, Email = email, Username = username },
                commandType: CommandType.StoredProcedure);

            return user ?? throw new ArgumentException("Wrong id, no such person");
        }

        public async Task UserUpdateAsync(User user)
        {
            using var connection = new MySqlConnection(sqlcon);

            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = "UserUpdate";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(nameof(User.IDUser), user.IDUser);
            cmd.Parameters.AddWithValue(nameof(User.TenantID), user.TenantID);
            cmd.Parameters.AddWithValue(nameof(User.Email), user.Email);
            cmd.Parameters.AddWithValue(nameof(User.Username), user.Username);
            cmd.Parameters.AddWithValue(nameof(User.DevicePin), user.DevicePin);
            cmd.Parameters.AddWithValue(nameof(User.FirstName), user.FirstName);
            cmd.Parameters.AddWithValue(nameof(User.LastName), user.LastName);
            cmd.Parameters.AddWithValue(nameof(User.Phone), user.Phone);
            cmd.Parameters.AddWithValue(nameof(User.UserGroupID), user.UserGroupID);
            cmd.Parameters.AddWithValue(nameof(User.Enabled), user.Enabled);

            await cmd.ExecuteNonQueryAsync();

        }

        public async Task<bool> UserSetPasswordAsync(string? email, UserSecret userSecret)
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = "UserSetPassword";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(nameof(User.Email), email);
            cmd.Parameters.AddWithValue(nameof(UserSecret.PwdHash), userSecret.PwdHash);
            cmd.Parameters.AddWithValue(nameof(UserSecret.PwdSalt), userSecret.PwdSalt);


            long rows = (long)(await cmd.ExecuteScalarAsync())!;

            if (rows > 0) { return true; }
            return false;
        }


        public async Task<UserSecret> UserSecretGetAsync(int? idUser, string? email, string? username)
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = "UserSecretGet";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(User.IDUser), idUser);
            cmd.Parameters.AddWithValue(nameof(User.Email), email);
            cmd.Parameters.AddWithValue(nameof(User.Username), username);

            using var dr = (MySqlDataReader)await cmd.ExecuteReaderAsync();
            if (dr.HasRows && await dr.ReadAsync())
            {
                UserSecret userSecret = new UserSecret();
                userSecret.PwdHash = dr[nameof(UserSecret.PwdHash)].ToString();
                userSecret.PwdSalt = dr[nameof(UserSecret.PwdSalt)].ToString();

                return userSecret;
            }

            throw new ArgumentException("Wrong id, no such device");

        }


        public async Task<IList<UserRole>> UserRoleGetAsync()
        {
            using var connection = new MySqlConnection(sqlcon);
            var rows = await connection.QueryAsync<UserRole>(
                "UserRoleGet", commandType: CommandType.StoredProcedure);
            return rows.AsList();
        }



        // END USER


        // Device
        public async Task DeviceAddAsync(Device device)
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = "DeviceAdd";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(Device.TenantID), device.TenantID);
            cmd.Parameters.AddWithValue(nameof(Device.DeviceTypeID), device.DeviceTypeID);
            cmd.Parameters.AddWithValue(nameof(Device.DeviceUnitID), device.DeviceUnitID);
            cmd.Parameters.AddWithValue(nameof(Device.DeviceUnitZoneID), device.DeviceUnitZoneID);

            cmd.Parameters.AddWithValue(nameof(Device.DeviceName), device.DeviceName);
            cmd.Parameters.AddWithValue(nameof(Device.MacAddress), device.MacAddress);
            cmd.Parameters.AddWithValue(nameof(Device.ApiId), device.ApiId);
            cmd.Parameters.AddWithValue(nameof(Device.ApiKey), device.ApiKey);
            cmd.Parameters.AddWithValue(nameof(Device.ServicePoint), device.ServicePoint);
            cmd.Parameters.AddWithValue(nameof(Device.DeviceTypeServiceID), device.DeviceTypeServiceID);
            cmd.Parameters.AddWithValue(nameof(Device.ConfigVersion), device.ConfigVersion);

            cmd.Parameters.AddWithValue(nameof(Device.SleepSeconds), device.SleepSeconds);
            cmd.Parameters.AddWithValue(nameof(Device.SleepDeepEnabled), device.SleepDeepEnabled);

            cmd.Parameters.AddWithValue(nameof(Device.DeviceSensorEnabled), device.DeviceSensorEnabled);
            cmd.Parameters.AddWithValue(nameof(Device.DeviceControllerEnabled), device.DeviceControllerEnabled);
            cmd.Parameters.AddWithValue(nameof(Device.BatteryEnabled), device.BatteryEnabled);

            cmd.Parameters.AddWithValue(nameof(Device.Debug), device.Debug);
            cmd.Parameters.AddWithValue(nameof(Device.Enabled), device.Enabled);

            await cmd.ExecuteNonQueryAsync();
        }
        public async Task DeviceDeleteAsync(int? idDevice, int? tenantID)
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = "DeviceDelete";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(Device.IDDevice), idDevice);
            cmd.Parameters.AddWithValue(nameof(Device.TenantID), tenantID);
            await cmd.ExecuteNonQueryAsync();

        }
        public async Task<IList<Device>> DevicesGetAsync(int? tenantID)
        {
            using var connection = new MySqlConnection(sqlcon);
            var rows = await connection.QueryAsync<Device>(
                "DevicesGet",
                new { TenantID = tenantID },
                commandType: CommandType.StoredProcedure);
            return rows.AsList();
        }

        public async Task<Device> DeviceGetAsync(int? tenantID, int? idDevice, string? apiId, string? macAddress)
        {
            using var connection = new MySqlConnection(sqlcon);
            var device = await connection.QuerySingleOrDefaultAsync<Device>(
                "DeviceGet",
                new { TenantID = tenantID, IDDevice = idDevice, ApiId = apiId, MacAddress = macAddress },
                commandType: CommandType.StoredProcedure);

            return device ?? new Device(); // empty device on no row, kept intentionally
        }

        public async Task<Device> DeviceGetByIdAsync(int? idDevice)
        {
            // No tenant filter by design - callers use this only to look up a device's real
            // TenantID for an ownership check, before deciding whether to allow the request.
            using var connection = new MySqlConnection(sqlcon);
            var device = await connection.QuerySingleOrDefaultAsync<Device>(
                "DeviceGetById",
                new { IDDevice = idDevice },
                commandType: CommandType.StoredProcedure);

            return device ?? new Device();
        }

        // DEVICE UPDATE
        public async Task DeviceUpdateAsync(Device? device)
        {


            using var connection = new MySqlConnection(sqlcon);

            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = "DeviceUpdate";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(nameof(Device.IDDevice), device.IDDevice);
            cmd.Parameters.AddWithValue(nameof(Device.TenantID), device.TenantID);
            cmd.Parameters.AddWithValue(nameof(Device.DeviceTypeID), device.DeviceTypeID);
            cmd.Parameters.AddWithValue(nameof(Device.DeviceTypeServiceID), device.DeviceTypeServiceID);
            cmd.Parameters.AddWithValue(nameof(Device.DeviceUnitID), device.DeviceUnitID);
            cmd.Parameters.AddWithValue(nameof(Device.DeviceUnitZoneID), device.DeviceUnitZoneID);

            cmd.Parameters.AddWithValue(nameof(Device.DeviceName), device.DeviceName);
            cmd.Parameters.AddWithValue(nameof(Device.ApiId), device.ApiId);
            cmd.Parameters.AddWithValue(nameof(Device.ApiKey), device.ApiKey);

            cmd.Parameters.AddWithValue(nameof(Device.ServicePoint), device.ServicePoint);
            cmd.Parameters.AddWithValue(nameof(Device.ServicePublicKey), device.ServicePublicKey);

            cmd.Parameters.AddWithValue(nameof(Device.SleepSeconds), device.SleepSeconds);
            cmd.Parameters.AddWithValue(nameof(Device.SleepDeepEnabled), device.SleepDeepEnabled);

            cmd.Parameters.AddWithValue(nameof(Device.DeviceSensorEnabled), device.DeviceSensorEnabled);
            cmd.Parameters.AddWithValue(nameof(Device.DeviceControllerEnabled), device.DeviceControllerEnabled);

            cmd.Parameters.AddWithValue(nameof(Device.BatteryEnabled), device.BatteryEnabled);
            cmd.Parameters.AddWithValue(nameof(Device.Enabled), device.Enabled);
            cmd.Parameters.AddWithValue(nameof(Device.Debug), device.Debug);
            cmd.Parameters.AddWithValue(nameof(Device.ConfigVersion), device.ConfigVersion);

            await cmd.ExecuteNonQueryAsync();

        }

        public async Task DeviceConfigControllerUpdateAsync(int? idDevice, DeviceConfigController? deviceConfigController)
        {


            using var connection = new MySqlConnection(sqlcon);

            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = "DeviceConfigControllerUpdate";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(nameof(Device.IDDevice), idDevice);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.IDDeviceConfigController), deviceConfigController.IDDeviceConfigController);

            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.TempLow), deviceConfigController.TempLow);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.TempHigh), deviceConfigController.TempHigh);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.HumidLow), deviceConfigController.HumidLow);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.HumidHigh), deviceConfigController.HumidHigh);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.MoistLow), deviceConfigController.MoistLow);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.MoistHigh), deviceConfigController.MoistHigh);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.LightLow), deviceConfigController.LightLow);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.LightHigh), deviceConfigController.LightHigh);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.WaterLow), deviceConfigController.WaterLow);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.WaterHigh), deviceConfigController.WaterHigh);

            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.VentilationIntervalEnabled), deviceConfigController.VentilationIntervalEnabled);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.VentilationInterval), deviceConfigController.VentilationInterval);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.VentilationIntervalLenght), deviceConfigController.VentilationIntervalLenght);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.LightIntervalEnabled), deviceConfigController.LightIntervalEnabled);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.LightInterval), deviceConfigController.LightInterval);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.LightIntervalLenght), deviceConfigController.LightIntervalLenght);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.HeatingIntervalEnabled), deviceConfigController.HeatingIntervalEnabled);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.HeatingInterval), deviceConfigController.HeatingInterval);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.HeatingIntervalLenght), deviceConfigController.HeatingIntervalLenght);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.WaterPumpIntervalEnabled), deviceConfigController.WaterPumpIntervalEnabled);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.WaterPumpInterval), deviceConfigController.WaterPumpInterval);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.WaterPumpIntervalLenght), deviceConfigController.WaterPumpIntervalLenght);


            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.RelayEnabled), deviceConfigController.RelayEnabled);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.Relay1), deviceConfigController.Relay1);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.Relay2), deviceConfigController.Relay2);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.Relay3), deviceConfigController.Relay3);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.Relay4), deviceConfigController.Relay4);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.Relay5), deviceConfigController.Relay5);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.Relay6), deviceConfigController.Relay6);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.Relay7), deviceConfigController.Relay7);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigController.Relay8), deviceConfigController.Relay8);


            await cmd.ExecuteNonQueryAsync();

        }

        public async Task DeviceConfigSensorUpdateAsync(int? idDevice, DeviceConfigSensor? deviceConfigSensor)
        {


            using var connection = new MySqlConnection(sqlcon);

            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = "DeviceConfigSensorUpdate";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(Device.IDDevice), idDevice);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigSensor.IDDeviceConfigSensor), deviceConfigSensor.IDDeviceConfigSensor);

            cmd.Parameters.AddWithValue(nameof(DeviceConfigSensor.SensorBattery), deviceConfigSensor.SensorBattery);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigSensor.SensorTemp), deviceConfigSensor.SensorTemp);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigSensor.SensorTempSoil), deviceConfigSensor.SensorTempSoil);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigSensor.SensorHumid), deviceConfigSensor.SensorHumid);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigSensor.SensorMoist), deviceConfigSensor.SensorMoist);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigSensor.SensorLight), deviceConfigSensor.SensorLight);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigSensor.SensorCo2), deviceConfigSensor.SensorCo2);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigSensor.SensorTvoc), deviceConfigSensor.SensorTvoc);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigSensor.SensorBarometer), deviceConfigSensor.SensorBarometer);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigSensor.SensorPH), deviceConfigSensor.SensorPH);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigSensor.SensorRainLevel), deviceConfigSensor.SensorRainLevel);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigSensor.SensorWaterLevel), deviceConfigSensor.SensorWaterLevel);
            cmd.Parameters.AddWithValue(nameof(DeviceConfigSensor.SensorWind), deviceConfigSensor.SensorWind);


            await cmd.ExecuteNonQueryAsync();

        }

        public async Task<bool> DeviceCheckMacAddressAsync(int? tenantID, string? macAddress)
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = "DeviceCheckMacAddress";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(Device.TenantID), tenantID);
            cmd.Parameters.AddWithValue(nameof(Device.MacAddress), macAddress);

            using var dr = (MySqlDataReader)await cmd.ExecuteReaderAsync();
            if (dr.HasRows)
            {
                return true;

            }
            else
            {
                return false;
            }
        }

        public async Task<DeviceConfigSensor?> DeviceConfigSensorGetAsync(int? deviceConfigSensorID)
        {
            using var connection = new MySqlConnection(sqlcon);
            var config = await connection.QuerySingleOrDefaultAsync<DeviceConfigSensor>(
                "DeviceConfigSensorGet",
                new { DeviceConfigSensorID = deviceConfigSensorID },
                commandType: CommandType.StoredProcedure);
            return config ?? new DeviceConfigSensor(); // empty config on no row, kept intentionally
        }

        public async Task<DeviceConfigController?> DeviceConfigControllerGetAsync(int? deviceConfigControllerID)
        {
            using var connection = new MySqlConnection(sqlcon);
            var config = await connection.QuerySingleOrDefaultAsync<DeviceConfigController>(
                "DeviceConfigControllerGet",
                new { DeviceConfigControllerID = deviceConfigControllerID },
                commandType: CommandType.StoredProcedure);
            return config ?? new DeviceConfigController(); // empty config on no row, kept intentionally
        }

        public async Task<Device> DeviceGetByDeviceConfigSensorIdAsync(int? deviceConfigSensorID)
        {
            // No tenant filter by design - used only to look up the owning device's TenantID
            // for an ownership check before returning the config to the caller.
            using var connection = new MySqlConnection(sqlcon);
            var device = await connection.QuerySingleOrDefaultAsync<Device>(
                "DeviceGetByDeviceConfigSensorId",
                new { DeviceConfigSensorID = deviceConfigSensorID },
                commandType: CommandType.StoredProcedure);
            return device ?? new Device();
        }

        public async Task<Device> DeviceGetByDeviceConfigControllerIdAsync(int? deviceConfigControllerID)
        {
            using var connection = new MySqlConnection(sqlcon);
            var device = await connection.QuerySingleOrDefaultAsync<Device>(
                "DeviceGetByDeviceConfigControllerId",
                new { DeviceConfigControllerID = deviceConfigControllerID },
                commandType: CommandType.StoredProcedure);
            return device ?? new Device();
        }



        // DEVICE TYPE LIST
        public async Task<IList<DeviceType>> DeviceTypeGetAsync()
        {
            using var connection = new MySqlConnection(sqlcon);
            var rows = await connection.QueryAsync<DeviceType>(
                "DeviceTypeGet", commandType: CommandType.StoredProcedure);
            return rows.AsList();
        }

        public async Task<IList<DeviceTypeService>> DeviceTypeServiceGetAsync()
        {
            using var connection = new MySqlConnection(sqlcon);
            var rows = await connection.QueryAsync<DeviceTypeService>(
                "DeviceTypeServiceGet", commandType: CommandType.StoredProcedure);
            return rows.AsList();
        }

        // TYPE RELAY
        public async Task<IList<DeviceTypeRelay>> DeviceTypeRelayGetAsync()
        {
            using var connection = new MySqlConnection(sqlcon);
            var rows = await connection.QueryAsync<DeviceTypeRelay>(
                "DeviceTypeRelayGet", commandType: CommandType.StoredProcedure);
            return rows.AsList();
        }

        // TYPE SENSOR
        public async Task<IList<DeviceTypeSensor>> DeviceTypeSensorGetAsync()
        {
            using var connection = new MySqlConnection(sqlcon);
            var rows = await connection.QueryAsync<DeviceTypeSensor>(
                "DeviceTypeSensorGet", commandType: CommandType.StoredProcedure);
            return rows.AsList();
        }
        // END DEVICE





        // SENSOR DATA START
        #region SensorData
        public async Task SensorDataPushAsync(JsonArray jsonArray) // SENSOR DATA START
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = "SensorDataPush";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("jsonData", jsonArray.ToString());

            await cmd.ExecuteNonQueryAsync();
        }
        public async Task<string> SensorDataGetAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport)
        {

            // IList<SensorData> sensorData = new List<SensorData>();
            string sensorDataResult;


            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = "SensorDataGet";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("deviceID", deviceID);
            cmd.Parameters.AddWithValue("tenantID", tenantID);
            cmd.Parameters.AddWithValue("timeRange", timeRange);
            cmd.Parameters.AddWithValue("timeMDMY", timeMDMY);
            cmd.Parameters.AddWithValue("buildReport", buildReport);

            using var dr = (MySqlDataReader)await cmd.ExecuteReaderAsync();

            if (await dr.ReadAsync())
            {

                sensorDataResult = dr["sensorDataResult"].ToString();
                return sensorDataResult;

            }


            return sensorDataResult="";
        }

        public async Task<IList<SensorDataReport>> SensorDataReportGetAsync(int? getData, int? deviceID, int? reportID)
        {
            using var connection = new MySqlConnection(sqlcon);
            // The proc returns different column sets for getData==0 vs getData>0; Dapper just
            // maps whichever columns are present (SensorData stays null for the summary case).
            var rows = await connection.QueryAsync<SensorDataReport>(
                "SensorDataReportGet",
                new { getData, deviceID, reportID },
                commandType: CommandType.StoredProcedure);
            return rows.AsList();
        }



        public async Task SensorDataDeleteAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY)
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = "SensorDataDelete";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("deviceID", deviceID);
            cmd.Parameters.AddWithValue("tenantID", tenantID);
            cmd.Parameters.AddWithValue("timeMDMY", timeMDMY);
            cmd.Parameters.AddWithValue("timeRange", timeRange);


            await cmd.ExecuteNonQueryAsync();
        }
        #endregion
        // END SENSOR


        // TENANT
        public async Task<bool> TenantGetAsync(string tenantName)
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = "TenantGet";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(Tenant.TenantName), tenantName);

            using var dr = (MySqlDataReader)await cmd.ExecuteReaderAsync();
            if (dr.HasRows)
            {
                return true;

            }
            else
            {
                return false;
            }
        }
        public async Task<int?> TenantGetIdAsync(string tenantName)
        {
            using var connection = new MySqlConnection(sqlcon);
            // Reuses the same TenantGet proc as TenantGetAsync (it already selects IDTenant),
            // just reads the id back instead of only checking whether a row exists.
            var tenant = await connection.QuerySingleOrDefaultAsync<Tenant>(
                "TenantGet",
                new { TenantName = tenantName },
                commandType: CommandType.StoredProcedure);
            return tenant?.IDTenant;
        }
        public async Task<int> TenantAddAsync(string tenantName)
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = "TenantAdd";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(nameof(Tenant.TenantName), tenantName);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync()); // retrieve single value from stored procedure
        }
        // END TENANT

        #region Group
        public async Task<IList<UserGroup>> UserGroupsGetAsync()
        {
            using var connection = new MySqlConnection(sqlcon);
            var rows = await connection.QueryAsync<UserGroup>(
                "UserGroupsGet", commandType: CommandType.StoredProcedure);
            return rows.AsList();
        }


        public async Task<UserGroup> UserGroupGetAsync(int? idUserGroup)
        {
            using var connection = new MySqlConnection(sqlcon);
            var group = await connection.QuerySingleOrDefaultAsync<UserGroup>(
                "UserGroupGet",
                new { IDUserGroup = idUserGroup },
                commandType: CommandType.StoredProcedure);

            return group ?? throw new ArgumentException("Wrong id, no such person");
        }

        public async Task UserGroupDeleteAsync(int? idUserGroup)
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = "UserGroupDelete";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(UserGroup.IDUserGroup), idUserGroup);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UserGroupAddAsync(UserGroup userGroup)
        {
            using var connection = new MySqlConnection(sqlcon);
            await connection.OpenAsync();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = "UserGroupAdd";
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(UserGroup.GroupName), userGroup.GroupName);
            cmd.Parameters.AddWithValue(nameof(UserGroup.UserRoleID), userGroup.UserRoleID);

            await cmd.ExecuteNonQueryAsync();
        }


        #endregion



    }
}
