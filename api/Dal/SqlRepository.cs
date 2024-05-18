using api.Models;
using MySql.Data.MySqlClient;

using System.Reflection;
using Humanizer;
using System;
using Org.BouncyCastle.Utilities;
using Microsoft.AspNetCore.Razor.Language.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Google.Protobuf.Compiler;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;
using System.Net.Mail;
using System.CodeDom;
using Microsoft.CodeAnalysis.FlowAnalysis.DataFlow;
using NuGet.Protocol;
using System.Text;
using api.Dal.Interface;
using System.Data;


namespace api.Dal
{
    // COLLAPSE CTRL+M+O <-----

    internal class SqlRepository : IRepository
    {

        private static string? sqlcon = Config.defaultSqlCon;
        private static int nullIntVal; // solving problem with int.TryParse
        private static double nullDoubleVal; // solving problem with double.TryParse
        private static bool nullBoolVal; // solving problem with bool.TryParse

        // QUERY 

        // AUTHENTICATION

        public ServerConfig ServerConfigGet(int idServerConfig = 1)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(ServerConfig.IDServerConfig), idServerConfig);

            using MySqlDataReader dr = cmd.ExecuteReader();
            if (dr.Read())
            {
                return ServerConfigRead(dr);
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

            ServerConfigAdd(generatedConfig);
            return generatedConfig;

            //throw new ArgumentException("Wrong id, no such server config");
        }

        private void ServerConfigAdd(ServerConfig serverConfig)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(nameof(ServerConfig.IDServerConfig), serverConfig.IDServerConfig);
            cmd.Parameters.AddWithValue(nameof(ServerConfig.ServerConfigName), serverConfig.ServerConfigName);
            cmd.Parameters.AddWithValue(nameof(ServerConfig.ConfigKey), serverConfig.ConfigKey);
            cmd.Parameters.AddWithValue(nameof(ServerConfig.PortHTTP), serverConfig.PortHTTP);
            cmd.Parameters.AddWithValue(nameof(ServerConfig.PortHTTPS), serverConfig.PortHTTPS);


            cmd.ExecuteNonQuery();
        }

        private void ServerConfigUpdate(ServerConfig serverConfig)
        {
            using var connection = new MySqlConnection(sqlcon);

            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(nameof(ServerConfig.ServerConfigName), serverConfig.ServerConfigName);
            cmd.Parameters.AddWithValue(nameof(ServerConfig.PortHTTP), serverConfig.PortHTTP);
            cmd.Parameters.AddWithValue(nameof(ServerConfig.PortHTTPS), serverConfig.PortHTTPS);

            cmd.Parameters.AddWithValue(nameof(ServerConfig.IDServerConfig), serverConfig.IDServerConfig);
            cmd.ExecuteNonQuery();

            // CAN NOT CHANGE ConfigKey, that would distrupt passwords!
        }

        private ServerConfig ServerConfigRead(MySqlDataReader dr) => new ServerConfig
        {
            IDServerConfig = (int)dr[nameof(ServerConfig.IDServerConfig)],
            ServerConfigName = dr[nameof(ServerConfig.ServerConfigName)].ToString(),
            ConfigKey = dr[nameof(ServerConfig.ConfigKey)].ToString(),
            PortHTTP = (int)dr[nameof(ServerConfig.PortHTTP)],
            PortHTTPS = (int)dr[nameof(ServerConfig.PortHTTPS)]
        };


        // USER
        public void UserAdd(User user, UserSecret userSecret)
        {

            //await connection.OpenAsync(); // ovo treba natjerati da radi!
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
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

            cmd.ExecuteNonQuery();

        }
        public bool UserDelete(int? idUser)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(User.IDUser), idUser);

            long rows = (long)cmd.ExecuteScalar();
            //using MySqlDataReader dr = cmd.ExecuteReader();

            if (rows > 0) { return true; }
            return false;
        }
        // All users
        public IList<User> UsersGet(int? tenantID)
        {
            IList<User> list = new List<User>();

            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(nameof(User.TenantID), tenantID);

            using MySqlDataReader dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                list.Add(UserRead(dr));
            }

            return list;
        }
        private User UserRead(MySqlDataReader dr) => new User
        {
            IDUser = int.TryParse(dr[nameof(User.IDUser)].ToString(), out nullIntVal) ? nullIntVal : null,
            TenantID = int.TryParse(dr[nameof(User.TenantID)].ToString(), out nullIntVal) ? nullIntVal : null,
            Email = dr[nameof(User.Email)].ToString(),
            Username = dr[nameof(User.Username)].ToString(),
            DevicePin = int.TryParse(dr[nameof(User.DevicePin)].ToString(), out nullIntVal) ? nullIntVal : null,
            FirstName = dr[nameof(User.FirstName)].ToString(),
            LastName = dr[nameof(User.LastName)].ToString(),
            Phone = dr[nameof(User.Phone)].ToString(),
            UserGroupID = int.TryParse(dr[nameof(User.UserGroupID)].ToString(), out nullIntVal) ? nullIntVal : null,
            UserRoleID = int.TryParse(dr[nameof(User.UserRoleID)].ToString(), out nullIntVal) ? nullIntVal : null,
            GroupName = dr[nameof(User.GroupName)].ToString(),
            Enabled = bool.TryParse(dr[nameof(User.Enabled)].ToString(), out nullBoolVal) ? nullBoolVal : null,

            DateCreated = (DateTime)dr[nameof(User.DateCreated)],
            DateModified = (DateTime)dr[nameof(User.DateModified)]
        };
        // Single user
        public User UserGet(int? idUser, string? email, string? username)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(User.IDUser), idUser);
            cmd.Parameters.AddWithValue(nameof(User.Email), email);
            cmd.Parameters.AddWithValue(nameof(User.Username), username);

            using MySqlDataReader dr = cmd.ExecuteReader();
            if (dr.Read())
            {
                return UserRead(dr);
            }

            throw new ArgumentException("Wrong id, no such person");

        }

        public void UserUpdate(User user)
        {
            using var connection = new MySqlConnection(sqlcon);

            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
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

            cmd.ExecuteNonQuery();

        }

        public bool UserSetPassword(string? email, UserSecret userSecret)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(nameof(User.Email), email);
            cmd.Parameters.AddWithValue(nameof(UserSecret.PwdHash), userSecret.PwdHash);
            cmd.Parameters.AddWithValue(nameof(UserSecret.PwdSalt), userSecret.PwdSalt);


            long rows = (long)cmd.ExecuteScalar();
            //using MySqlDataReader dr = cmd.ExecuteReader();

            if (rows > 0) { return true; }
            return false;
        }


        // UDRI OVDJE ZA PASSWORD
        public UserSecret UserSecretGet(int? idUser, string? email, string? username)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(User.IDUser), idUser);
            cmd.Parameters.AddWithValue(nameof(User.Email), email);
            cmd.Parameters.AddWithValue(nameof(User.Username), username);

            using MySqlDataReader dr = cmd.ExecuteReader();
            if (dr.HasRows && dr.Read())
            {
                UserSecret userSecret = new UserSecret();
                userSecret.PwdHash = dr[nameof(UserSecret.PwdHash)].ToString();
                userSecret.PwdSalt = dr[nameof(UserSecret.PwdSalt)].ToString();

                return userSecret;
            }

            throw new ArgumentException("Wrong id, no such device");

        }


        public IList<UserRole> UserRoleGet()
        {
            IList<UserRole> userRoles = new List<UserRole>();
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            using MySqlDataReader dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                userRoles.Add(UserRoleRead(dr));
            }

            return userRoles;

        }
        private UserRole UserRoleRead(MySqlDataReader dr) => new UserRole
        {
            IDUserRole = int.TryParse(dr[nameof(UserRole.IDUserRole)].ToString(), out nullIntVal) ? nullIntVal : null,
            RoleName = dr[nameof(UserRole.RoleName)].ToString(),
            RoleScopeID = int.TryParse(dr[nameof(UserRole.RoleScopeID)].ToString(), out nullIntVal) ? nullIntVal : null
        };



        // END USER


        // Device
        public void DeviceAdd(Device device)
        {
            //await connection.OpenAsync(); // ovo treba natjerati da radi!
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
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

            cmd.ExecuteNonQuery();
        }
        public void DeviceDelete(int? idDevice)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(Device.IDDevice), idDevice);
            cmd.ExecuteNonQuery();

        }
        public IList<Device> DevicesGet(int? tenantID)
        {
            IList<Device> list = new List<Device>();

            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(nameof(Device.TenantID), tenantID);

            using MySqlDataReader dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                list.Add(DeviceRead(dr));
            }

            return list;
        }
        
        public Device DeviceGet(int? tenantID, int? idDevice, string? apiId, string? macAddress)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(Device.TenantID), tenantID);
            cmd.Parameters.AddWithValue(nameof(Device.IDDevice), idDevice);
            cmd.Parameters.AddWithValue(nameof(Device.ApiId), apiId);
            cmd.Parameters.AddWithValue(nameof(Device.MacAddress), macAddress);

            using MySqlDataReader dr = cmd.ExecuteReader();
            if (dr.Read())
            {
                return DeviceRead(dr);
            }
            else
            {
                Device device = new Device();
                return device; // vracamo prazni device namjerno
            }

        }

        private Device DeviceRead(MySqlDataReader dr) => new Device
        {
            ConfigVersion = int.TryParse(dr[nameof(Device.ConfigVersion)].ToString(), out nullIntVal) ? nullIntVal : null,

            IDDevice = int.TryParse(dr[nameof(Device.IDDevice)].ToString(), out nullIntVal) ? nullIntVal : null,
            TenantID = int.TryParse(dr[nameof(Device.TenantID)].ToString(), out nullIntVal) ? nullIntVal : null,
            DeviceTypeID = int.TryParse(dr[nameof(Device.DeviceTypeID)].ToString(), out nullIntVal) ? nullIntVal : null,
            DeviceUnitID = int.TryParse(dr[nameof(Device.DeviceUnitID)].ToString(), out nullIntVal) ? nullIntVal : null,
            DeviceUnitZoneID = int.TryParse(dr[nameof(Device.DeviceUnitZoneID)].ToString(), out nullIntVal) ? nullIntVal : null,
            DeviceConfigSensorID = int.TryParse(dr[nameof(Device.DeviceConfigSensorID)].ToString(), out nullIntVal) ? nullIntVal : null,
            DeviceConfigControllerID = int.TryParse(dr[nameof(Device.DeviceConfigControllerID)].ToString(), out nullIntVal) ? nullIntVal : null,
            DeviceTypeServiceID = int.TryParse(dr[nameof(Device.DeviceTypeServiceID)].ToString(), out nullIntVal) ? nullIntVal : null,

            DeviceName = dr[nameof(Device.DeviceName)].ToString(),
            MacAddress = dr[nameof(Device.MacAddress)].ToString(),

            ApiId = dr[nameof(Device.ApiId)].ToString(),
            ApiKey = dr[nameof(Device.ApiKey)].ToString(),
            ServicePoint = dr[nameof(Device.ServicePoint)].ToString(),
            ServicePublicKey = dr[nameof(Device.ServicePublicKey)].ToString(),

            SleepSeconds = int.TryParse(dr[nameof(Device.SleepSeconds)].ToString(), out nullIntVal) ? nullIntVal : null,
            SleepDeepEnabled = bool.TryParse(dr[nameof(Device.SleepDeepEnabled)].ToString(), out nullBoolVal) ? nullBoolVal : null,

            DeviceSensorEnabled = bool.TryParse(dr[nameof(Device.DeviceSensorEnabled)].ToString(), out nullBoolVal) ? nullBoolVal : null,
            DeviceControllerEnabled = bool.TryParse(dr[nameof(Device.DeviceControllerEnabled)].ToString(), out nullBoolVal) ? nullBoolVal : null,

            BatteryEnabled = bool.TryParse(dr[nameof(Device.BatteryEnabled)].ToString(), out nullBoolVal) ? nullBoolVal : null,

            Debug = bool.TryParse(dr[nameof(Device.Debug)].ToString(), out nullBoolVal) ? nullBoolVal : null,
            Reboot = bool.TryParse(dr[nameof(Device.Reboot)].ToString(), out nullBoolVal) ? nullBoolVal : null,
            Reset = bool.TryParse(dr[nameof(Device.Reset)].ToString(), out nullBoolVal) ? nullBoolVal : null,
            FirmwareUpdate = bool.TryParse(dr[nameof(Device.FirmwareUpdate)].ToString(), out nullBoolVal) ? nullBoolVal : null,

            Enabled = bool.TryParse(dr[nameof(Device.Enabled)].ToString(), out nullBoolVal) ? nullBoolVal : null,

            DateCreated = (DateTime)dr[nameof(Device.DateCreated)],
            DateModified = (DateTime)dr[nameof(Device.DateModified)]
        };

        // DEVICE UPDATE
        public void DeviceUpdate(Device? device)
        {


            using var connection = new MySqlConnection(sqlcon);

            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
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

            cmd.ExecuteNonQuery();

        }

        public void DeviceConfigControllerUpdate(int? idDevice, DeviceConfigController? deviceConfigController)
        {


            using var connection = new MySqlConnection(sqlcon);

            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
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


            cmd.ExecuteNonQuery();

        }

        public void DeviceConfigSensorUpdate(int? idDevice, DeviceConfigSensor? deviceConfigSensor)
        {


            using var connection = new MySqlConnection(sqlcon);

            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
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


            cmd.ExecuteNonQuery();

        }

        public bool DeviceCheckMacAddress(int? tenantID, string? macAddress)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(Device.TenantID), tenantID);
            cmd.Parameters.AddWithValue(nameof(Device.MacAddress), macAddress);

            using MySqlDataReader dr = cmd.ExecuteReader();
            if (dr.HasRows)
            {
                return true;

            }
            else
            {
                return false;
            }

            throw new ArgumentException("Wrong id, no such device");
        }

        public DeviceConfigSensor? DeviceConfigSensorGet(int? deviceConfigSensorID)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(Device.DeviceConfigSensorID), deviceConfigSensorID);

            using MySqlDataReader dr = cmd.ExecuteReader();
            if (dr.Read())
            {
                return DeviceConfigSensorRead(dr);
            }
            else
            {
                DeviceConfigSensor deviceConfigSensor = new DeviceConfigSensor();
                return deviceConfigSensor; // vracamo prazni device namjerno
            }
        }

        private DeviceConfigSensor DeviceConfigSensorRead(MySqlDataReader dr) => new DeviceConfigSensor
        {
            IDDeviceConfigSensor = int.TryParse(dr[nameof(DeviceConfigSensor.IDDeviceConfigSensor)].ToString(), out nullIntVal) ? nullIntVal : null,
            SensorBattery = int.TryParse(dr[nameof(DeviceConfigSensor.SensorBattery)].ToString(), out nullIntVal) ? nullIntVal : null,
            SensorTemp = int.TryParse(dr[nameof(DeviceConfigSensor.SensorTemp)].ToString(), out nullIntVal) ? nullIntVal : null,
            SensorTempSoil = int.TryParse(dr[nameof(DeviceConfigSensor.SensorTempSoil)].ToString(), out nullIntVal) ? nullIntVal : null,
            SensorHumid = int.TryParse(dr[nameof(DeviceConfigSensor.SensorHumid)].ToString(), out nullIntVal) ? nullIntVal : null,
            SensorMoist = int.TryParse(dr[nameof(DeviceConfigSensor.SensorMoist)].ToString(), out nullIntVal) ? nullIntVal : null,
            SensorLight = int.TryParse(dr[nameof(DeviceConfigSensor.SensorLight)].ToString(), out nullIntVal) ? nullIntVal : null,
            SensorCo2 = int.TryParse(dr[nameof(DeviceConfigSensor.SensorCo2)].ToString(), out nullIntVal) ? nullIntVal : null,
            SensorTvoc = int.TryParse(dr[nameof(DeviceConfigSensor.SensorTvoc)].ToString(), out nullIntVal) ? nullIntVal : null,
            SensorBarometer = int.TryParse(dr[nameof(DeviceConfigSensor.SensorBarometer)].ToString(), out nullIntVal) ? nullIntVal : null,
            SensorPH = int.TryParse(dr[nameof(DeviceConfigSensor.SensorPH)].ToString(), out nullIntVal) ? nullIntVal : null,
            SensorRainLevel = int.TryParse(dr[nameof(DeviceConfigSensor.SensorRainLevel)].ToString(), out nullIntVal) ? nullIntVal : null,
            SensorWaterLevel = int.TryParse(dr[nameof(DeviceConfigSensor.SensorWaterLevel)].ToString(), out nullIntVal) ? nullIntVal : null,
            SensorWind = int.TryParse(dr[nameof(DeviceConfigSensor.SensorWind)].ToString(), out nullIntVal) ? nullIntVal : null


        };

        public DeviceConfigController? DeviceConfigControllerGet(int? deviceConfigControllerID)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(Device.DeviceConfigControllerID), deviceConfigControllerID);

            using MySqlDataReader dr = cmd.ExecuteReader();
            if (dr.Read())
            {
                return DeviceConfigControllerRead(dr);
            }
            else
            {
                DeviceConfigController deviceConfigController = new DeviceConfigController();
                return deviceConfigController; // vracamo prazni device namjerno
                //throw new ArgumentException("Wrong id, no such person");

            }
        }
        private DeviceConfigController DeviceConfigControllerRead(MySqlDataReader dr) => new DeviceConfigController
        {
            IDDeviceConfigController = int.TryParse(dr[nameof(DeviceConfigController.IDDeviceConfigController)].ToString(), out nullIntVal) ? nullIntVal : null,

            TempLow = double.TryParse(dr[nameof(DeviceConfigController.TempLow)].ToString(), out nullDoubleVal) ? nullDoubleVal : null,
            TempHigh = double.TryParse(dr[nameof(DeviceConfigController.TempHigh)].ToString(), out nullDoubleVal) ? nullDoubleVal : null,
            HumidLow = double.TryParse(dr[nameof(DeviceConfigController.HumidLow)].ToString(), out nullDoubleVal) ? nullDoubleVal : null,
            HumidHigh = double.TryParse(dr[nameof(DeviceConfigController.HumidHigh)].ToString(), out nullDoubleVal) ? nullDoubleVal : null,
            MoistLow = double.TryParse(dr[nameof(DeviceConfigController.MoistLow)].ToString(), out nullDoubleVal) ? nullDoubleVal : null,
            MoistHigh = double.TryParse(dr[nameof(DeviceConfigController.MoistHigh)].ToString(), out nullDoubleVal) ? nullDoubleVal : null,
            LightLow = double.TryParse(dr[nameof(DeviceConfigController.LightLow)].ToString(), out nullDoubleVal) ? nullDoubleVal : null,
            LightHigh = double.TryParse(dr[nameof(DeviceConfigController.LightHigh)].ToString(), out nullDoubleVal) ? nullDoubleVal : null,
            WaterLow = double.TryParse(dr[nameof(DeviceConfigController.WaterLow)].ToString(), out nullDoubleVal) ? nullDoubleVal : null,
            WaterHigh = double.TryParse(dr[nameof(DeviceConfigController.WaterHigh)].ToString(), out nullDoubleVal) ? nullDoubleVal : null,

            VentilationIntervalEnabled = bool.TryParse(dr[nameof(DeviceConfigController.VentilationIntervalEnabled)].ToString(), out nullBoolVal) ? nullBoolVal : null,
            VentilationInterval = int.TryParse(dr[nameof(DeviceConfigController.VentilationInterval)].ToString(), out nullIntVal) ? nullIntVal : null,
            VentilationIntervalLenght = int.TryParse(dr[nameof(DeviceConfigController.VentilationIntervalLenght)].ToString(), out nullIntVal) ? nullIntVal : null,
            LightIntervalEnabled = bool.TryParse(dr[nameof(DeviceConfigController.LightIntervalEnabled)].ToString(), out nullBoolVal) ? nullBoolVal : null,
            LightInterval = int.TryParse(dr[nameof(DeviceConfigController.LightInterval)].ToString(), out nullIntVal) ? nullIntVal : null,
            LightIntervalLenght = int.TryParse(dr[nameof(DeviceConfigController.LightIntervalLenght)].ToString(), out nullIntVal) ? nullIntVal : null,
            HeatingIntervalEnabled = bool.TryParse(dr[nameof(DeviceConfigController.HeatingIntervalEnabled)].ToString(), out nullBoolVal) ? nullBoolVal : null,
            HeatingInterval = int.TryParse(dr[nameof(DeviceConfigController.HeatingInterval)].ToString(), out nullIntVal) ? nullIntVal : null,
            HeatingIntervalLenght = int.TryParse(dr[nameof(DeviceConfigController.HeatingIntervalLenght)].ToString(), out nullIntVal) ? nullIntVal : null,
            WaterPumpIntervalEnabled = bool.TryParse(dr[nameof(DeviceConfigController.WaterPumpIntervalEnabled)].ToString(), out nullBoolVal) ? nullBoolVal : null,
            WaterPumpInterval = int.TryParse(dr[nameof(DeviceConfigController.WaterPumpInterval)].ToString(), out nullIntVal) ? nullIntVal : null,
            WaterPumpIntervalLenght = int.TryParse(dr[nameof(DeviceConfigController.WaterPumpIntervalLenght)].ToString(), out nullIntVal) ? nullIntVal : null,

            RelayEnabled = bool.TryParse(dr[nameof(DeviceConfigController.RelayEnabled)].ToString(), out nullBoolVal) ? nullBoolVal : null,
            Relay1 = int.TryParse(dr[nameof(DeviceConfigController.Relay1)].ToString(), out nullIntVal) ? nullIntVal : null,
            Relay2 = int.TryParse(dr[nameof(DeviceConfigController.Relay2)].ToString(), out nullIntVal) ? nullIntVal : null,
            Relay3 = int.TryParse(dr[nameof(DeviceConfigController.Relay3)].ToString(), out nullIntVal) ? nullIntVal : null,
            Relay4 = int.TryParse(dr[nameof(DeviceConfigController.Relay4)].ToString(), out nullIntVal) ? nullIntVal : null,
            Relay5 = int.TryParse(dr[nameof(DeviceConfigController.Relay5)].ToString(), out nullIntVal) ? nullIntVal : null,
            Relay6 = int.TryParse(dr[nameof(DeviceConfigController.Relay6)].ToString(), out nullIntVal) ? nullIntVal : null,
            Relay7 = int.TryParse(dr[nameof(DeviceConfigController.Relay7)].ToString(), out nullIntVal) ? nullIntVal : null,
            Relay8 = int.TryParse(dr[nameof(DeviceConfigController.Relay8)].ToString(), out nullIntVal) ? nullIntVal : null

        };

        

        // DEVICE TYPE LIST
        public IList<DeviceType> DeviceTypeGet()
        {
            IList<DeviceType> deviceType = new List<DeviceType>();
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            using MySqlDataReader dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                deviceType.Add(DeviceTypeRead(dr));
            }

            return deviceType;
        }

        private DeviceType DeviceTypeRead(MySqlDataReader dr) => new DeviceType
        {
            IDDeviceType = int.TryParse(dr[nameof(DeviceType.IDDeviceType)].ToString(), out nullIntVal) ? nullIntVal : null,
            DeviceTypeName = dr[nameof(DeviceType.DeviceTypeName)].ToString(),
            SensorEnabled = bool.TryParse(dr[nameof(DeviceType.SensorEnabled)].ToString(), out nullBoolVal) ? nullBoolVal : null,
            ControllerEnabled = bool.TryParse(dr[nameof(DeviceType.ControllerEnabled)].ToString(), out nullBoolVal) ? nullBoolVal : null
        };

        public IList<DeviceTypeService> DeviceTypeServiceGet()
        {
            IList<DeviceTypeService> deviceTypeService = new List<DeviceTypeService>();
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            using MySqlDataReader dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                deviceTypeService.Add(DeviceTypeServiceRead(dr));
            }

            return deviceTypeService;
        }

        private DeviceTypeService DeviceTypeServiceRead(MySqlDataReader dr) => new DeviceTypeService
        {
            IDDeviceTypeService = int.TryParse(dr[nameof(DeviceTypeService.IDDeviceTypeService)].ToString(), out nullIntVal) ? nullIntVal : null,
            ServiceType = dr[nameof(DeviceTypeService.ServiceType)].ToString(),
        };



        // TYPE RELAY
        public IList<DeviceTypeRelay> DeviceTypeRelayGet()
        {
            IList<DeviceTypeRelay> deviceTypeRelay = new List<DeviceTypeRelay>();
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            using MySqlDataReader dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                deviceTypeRelay.Add(DeviceTypeRelayRead(dr));
            }

            return deviceTypeRelay;
        }

        private DeviceTypeRelay DeviceTypeRelayRead(MySqlDataReader dr) => new DeviceTypeRelay
        {
            IDDeviceTypeRelay = int.TryParse(dr[nameof(DeviceTypeRelay.IDDeviceTypeRelay)].ToString(), out nullIntVal) ? nullIntVal : null,
            RelayName = dr[nameof(DeviceTypeRelay.RelayName)].ToString(),
        };


        // TYPE SENSOR
        public IList<DeviceTypeSensor> DeviceTypeSensorGet()
        {
            IList<DeviceTypeSensor> deviceTypeSensor = new List<DeviceTypeSensor>();
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            using MySqlDataReader dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                deviceTypeSensor.Add(DeviceTypeSensorRead(dr));
            }

            return deviceTypeSensor;
        }

        private DeviceTypeSensor DeviceTypeSensorRead(MySqlDataReader dr) => new DeviceTypeSensor
        {
            IDDeviceTypeSensor = int.TryParse(dr[nameof(DeviceTypeSensor.IDDeviceTypeSensor)].ToString(), out nullIntVal) ? nullIntVal : null,
            SensorName = dr[nameof(DeviceTypeSensor.SensorName)].ToString(),
            SensorDescription = dr[nameof(DeviceTypeSensor.SensorDescription)].ToString(),
            Battery = int.TryParse(dr[nameof(DeviceTypeSensor.Battery)].ToString(), out nullIntVal) ? nullIntVal : null,
            Temperature = int.TryParse(dr[nameof(DeviceTypeSensor.Temperature)].ToString(), out nullIntVal) ? nullIntVal : null,
            TemperatureSoil = int.TryParse(dr[nameof(DeviceTypeSensor.TemperatureSoil)].ToString(), out nullIntVal) ? nullIntVal : null,
            Humidity = int.TryParse(dr[nameof(DeviceTypeSensor.Humidity)].ToString(), out nullIntVal) ? nullIntVal : null,
            Moisture = int.TryParse(dr[nameof(DeviceTypeSensor.Moisture)].ToString(), out nullIntVal) ? nullIntVal : null,
            Light = int.TryParse(dr[nameof(DeviceTypeSensor.Light)].ToString(), out nullIntVal) ? nullIntVal : null,
            Co2 = int.TryParse(dr[nameof(DeviceTypeSensor.Co2)].ToString(), out nullIntVal) ? nullIntVal : null,
            Tvoc = int.TryParse(dr[nameof(DeviceTypeSensor.Tvoc)].ToString(), out nullIntVal) ? nullIntVal : null,
            Barometer = int.TryParse(dr[nameof(DeviceTypeSensor.Barometer)].ToString(), out nullIntVal) ? nullIntVal : null,
            WaterPH = int.TryParse(dr[nameof(DeviceTypeSensor.WaterPH)].ToString(), out nullIntVal) ? nullIntVal : null,
            WaterTankLevel = int.TryParse(dr[nameof(DeviceTypeSensor.WaterTankLevel)].ToString(), out nullIntVal) ? nullIntVal : null,
            RainLevel = int.TryParse(dr[nameof(DeviceTypeSensor.RainLevel)].ToString(), out nullIntVal) ? nullIntVal : null,
            Wind = int.TryParse(dr[nameof(DeviceTypeSensor.Wind)].ToString(), out nullIntVal) ? nullIntVal : null,
        };
        // END DEVICE





        // SENSOR DATA START
        #region SensorData
        public void SensorDataPush(JsonArray jsonArray) // SENSOR DATA START
        {
            //await connection.OpenAsync(); // ovo treba natjerati da radi!
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("jsonData", jsonArray.ToString());

            cmd.ExecuteNonQuery();
        }
        public string SensorDataGet(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport)
        {

            // IList<SensorData> sensorData = new List<SensorData>();
            string sensorDataResult;


            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("deviceID", deviceID);
            cmd.Parameters.AddWithValue("tenantID", tenantID);
            cmd.Parameters.AddWithValue("timeRange", timeRange);
            cmd.Parameters.AddWithValue("timeMDMY", timeMDMY);
            cmd.Parameters.AddWithValue("buildReport", buildReport);

            using MySqlDataReader dr = cmd.ExecuteReader();

            if (dr.Read())
            {

                sensorDataResult = dr["sensorDataResult"].ToString();
                return sensorDataResult;

            }


            return sensorDataResult="";
        }

        public IList<SensorDataReport> SensorDataReportGet(int? getData, int? deviceID, int? reportID)
        {
            IList<SensorDataReport> sensorDataReport = new List<SensorDataReport>();

            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("getData", getData);
            cmd.Parameters.AddWithValue("deviceID", deviceID);
            cmd.Parameters.AddWithValue("reportID", reportID);



            using MySqlDataReader dr = cmd.ExecuteReader();

            if (getData==0)
            {
                while (dr.Read())
                {
                    sensorDataReport.Add(SensorDataReportsRead(dr));
                }
            }
            else
            {
                while (dr.Read())
                {
                    sensorDataReport.Add(SensorDataReportRead(dr));
                }
            }

            return sensorDataReport;
        }

        private SensorDataReport SensorDataReportsRead(MySqlDataReader dr) => new SensorDataReport
        {

            IDSensorDataReport = int.TryParse(dr[nameof(SensorDataReport.IDSensorDataReport)].ToString(), out nullIntVal) ? nullIntVal : null,
            DeviceID = int.TryParse(dr[nameof(SensorDataReport.DeviceID)].ToString(), out nullIntVal) ? nullIntVal : null,
            ReportName = dr[nameof(SensorDataReport.ReportName)].ToString()

        };

        private SensorDataReport SensorDataReportRead(MySqlDataReader dr) => new SensorDataReport
        {

            IDSensorDataReport = int.TryParse(dr[nameof(SensorDataReport.IDSensorDataReport)].ToString(), out nullIntVal) ? nullIntVal : null,
            DeviceID = int.TryParse(dr[nameof(SensorDataReport.DeviceID)].ToString(), out nullIntVal) ? nullIntVal : null,
            ReportName = dr[nameof(SensorDataReport.ReportName)].ToString(),
            SensorData = dr[nameof(SensorDataReport.SensorData)].ToString()

        };



        public void SensorDataDelete(int? tenantID, int? deviceID, int? timeMDMY, int? timeRange)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("deviceID", deviceID);
            cmd.Parameters.AddWithValue("tenantID", tenantID);
            cmd.Parameters.AddWithValue("timeMDMY", timeMDMY);
            cmd.Parameters.AddWithValue("timeRange", timeRange);


            cmd.ExecuteNonQuery();
        }
        #endregion
        // END SENSOR


        // TENANT
        public bool TenantGet(string tenantName)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(Tenant.TenantName), tenantName);

            using MySqlDataReader dr = cmd.ExecuteReader();
            if (dr.HasRows)
            {
                return true;

            }
            else
            {
                return false;
            }

            throw new ArgumentException("Wrong id, no such person");

        }
        public int TenantAdd(string tenantName)
        {
            //await connection.OpenAsync(); // ovo treba natjerati da radi!
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue(nameof(Tenant.TenantName), tenantName);

            return Convert.ToInt32(cmd.ExecuteScalar()); // retreive single value from stored procedure
            /*using MySqlDataReader dr = cmd.ExecuteReader();

            
            if (dr.HasRows)
            {
                return dr.GetInt32(0); //0 is row index

            }
            else
            {
                throw new ArgumentException("Failed to create new tenant");
            }
            */

        }
        // END TENANT

        #region Group
        public IList<UserGroup> UserGroupsGet()
        {
            IList<UserGroup> deviceTypeRelay = new List<UserGroup>();
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            using MySqlDataReader dr = cmd.ExecuteReader();
            while (dr.Read())
            {
                deviceTypeRelay.Add(UserGroupRead(dr));
            }

            return deviceTypeRelay;
        }


        public UserGroup UserGroupGet(int? idUserGroup)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(UserGroup.IDUserGroup), idUserGroup);

            using MySqlDataReader dr = cmd.ExecuteReader();
            if (dr.Read())
            {
                return UserGroupRead(dr);
            }

            throw new ArgumentException("Wrong id, no such person");

        }



        private UserGroup UserGroupRead(MySqlDataReader dr) => new UserGroup
        {
            IDUserGroup = int.TryParse(dr[nameof(UserGroup.IDUserGroup)].ToString(), out nullIntVal) ? nullIntVal : null,
            GroupName = dr[nameof(UserGroup.GroupName)].ToString(),
            UserRoleID = int.TryParse(dr[nameof(UserGroup.UserRoleID)].ToString(), out nullIntVal) ? nullIntVal : null,
            RoleName = dr[nameof(UserGroup.RoleName)].ToString(),
        };

        public void UserGroupDelete(int? idUserGroup)
        {
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();
            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(UserGroup.IDUserGroup), idUserGroup);
            cmd.ExecuteNonQuery();
        }

        public void UserGroupAdd(UserGroup userGroup)
        {
            //await connection.OpenAsync(); // ovo treba natjerati da radi!
            using var connection = new MySqlConnection(sqlcon);
            connection.Open();
            using MySqlCommand cmd = connection.CreateCommand();

            cmd.CommandText = MethodBase.GetCurrentMethod()?.Name;
            cmd.CommandType = System.Data.CommandType.StoredProcedure;

            cmd.Parameters.AddWithValue(nameof(UserGroup.GroupName), userGroup.GroupName);
            cmd.Parameters.AddWithValue(nameof(UserGroup.UserRoleID), userGroup.UserRoleID);

            cmd.ExecuteNonQuery();
        }


        #endregion



    }
}
