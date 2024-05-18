using api.Dal.Interface;
using api.Models;
using api.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.MSIdentity.Shared;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;
using MySqlX.XDevAPI;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.Ocsp;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Web;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace api.Dal
{
    public class ApiRepository : IApi
    {
        private static string? apiService = Config.apiService;


        private static readonly string UserLoginPostApi = "/api/User/Login";

        private static readonly string UsersGetApi = "/api/User/All";
        private static readonly string UserGetApi = "/api/User/?";
        private static readonly string UserDeleteApi = "/api/User/?";
        private static readonly string UserPostApi = "/api/User";
        private static readonly string UserPutApi = "/api/User";
        private static readonly string UserRoleGetApi = "/api/User/Roles";

        private static readonly string UsersGroupsGetApi = "/api/User/Group/All";
        private static readonly string UsersGroupGetApi = "/api/User/Group?";
        private static readonly string UsersGroupPostApi = "/api/User/Group";
        private static readonly string UsersGroupDeleteApi = "/api/User/Group?";

        private static readonly string DevicesGetApi = "/api/Device/All";
        private static readonly string DeviceGetApi = "/api/Device?";
        private static readonly string DeviceDeleteApi = "/api/Device/?";
        private static readonly string DevicePutApi = "/api/Device";
        private static readonly string DeviceConfigSensorGetApi = "/api/Device/Sensor?";
        private static readonly string DeviceConfigSensorPutApi = "/api/Device/Sensor?";
        private static readonly string DeviceConfigControllerGetApi = "/api/Device/Controller?";
        private static readonly string DeviceConfigControllerPutApi = "/api/Device/Controller?";

        private static readonly string DeviceTypeGetApi = "/api/Device/Type";
        private static readonly string DeviceTypeRelayGetApi = "/api/Device/TypeRelay";
        private static readonly string DeviceTypeSensorGetApi = "/api/Device/TypeSensor";
        private static readonly string DeviceTypeServiceGetApi = "/api/Device/TypeService";

        private static readonly string SensorDataGetApi = "/api/SensorData?";
        private static readonly string SensorDataReportGetApi = "/api/SensorData/Report?";


        private static HttpClient httpClient = new()
        {
            //BaseAddress = new Uri("http://localhost:5151"),
            BaseAddress = new Uri(apiService)
        };

        // dependancy injection
        private readonly HttpClient _client;
        public ApiRepository(HttpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ApiRepository()
        {

        }


        #region Device

        public async Task<IEnumerable<Device>> DevicesGet(string jwtKey)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 
            var response = await httpClient.GetAsync(DevicesGetApi); // send async za parametre

            return await response.ReadContentAsync<IEnumerable<Device>>();
        }
        public async Task<Device> DeviceGet(string jwtKey, int? idDevice, string? apiId, string? macAddress)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 

            var query = HttpUtility.ParseQueryString(string.Empty);
            if (idDevice != null) { query["idDevice"] = idDevice.ToString(); }
            if (apiId != null) { query["apiId"] = apiId.ToString(); }
            if (macAddress != null) { query["macAddress"] = macAddress.ToString(); }

            string queryString = DeviceGetApi + query.ToString();

            var response = await httpClient.GetAsync(queryString);

            return await response.ReadContentAsync<Device>();
        }
        public async Task<bool> DeviceUpdate(string jwtKey, Device? device)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 

            string? jsonData = JsonConvert.SerializeObject(device);
            HttpContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var response = await httpClient.PutAsync(DevicePutApi, content); // send async za parametre

            return await response.ReadContentAsync<bool>();
        }
        public async Task<bool> DeviceDelete(string jwtKey, int? idDevice)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 
            var query = HttpUtility.ParseQueryString(string.Empty);
            if (idDevice != null) { query["idDevice"] = idDevice.ToString(); }
            string queryString = DeviceDeleteApi + query.ToString();

            var response = await httpClient.DeleteAsync(queryString); // send async za parametre
            return await response.ReadContentAsync<bool>();
        }


        // Config
        public async Task<DeviceConfigSensor> DeviceConfigSensorGet(string jwtKey, int? deviceConfigSensorID)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 

            var query = HttpUtility.ParseQueryString(string.Empty);
            query["deviceConfigSensorID"] = deviceConfigSensorID.ToString(); 

            string queryString = DeviceConfigSensorGetApi + query.ToString();

            var response = await httpClient.GetAsync(queryString);

            return await response.ReadContentAsync<DeviceConfigSensor>();
        }
        public async Task<DeviceConfigController> DeviceConfigControllerGet(string jwtKey, int? deviceConfigControllerID)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 

            var query = HttpUtility.ParseQueryString(string.Empty);
            query["deviceConfigControllerID"] = deviceConfigControllerID.ToString();

            string queryString = DeviceConfigControllerGetApi + query.ToString();

            var response = await httpClient.GetAsync(queryString);

            return await response.ReadContentAsync<DeviceConfigController>();
        }

        public async Task<bool> DeviceConfigSensorUpdate(string jwtKey, DeviceUpdate deviceUpdate)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 

            string? jsonData = JsonConvert.SerializeObject(deviceUpdate);
            HttpContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var response = await httpClient.PutAsync(DeviceConfigSensorPutApi, content); // send async za parametre

            return await response.ReadContentAsync<bool>();
        }
        public async Task<bool> DeviceConfigControllerUpdate(string jwtKey, DeviceUpdate deviceUpdate)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 

            string? jsonData = JsonConvert.SerializeObject(deviceUpdate);
            HttpContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var response = await httpClient.PutAsync(DeviceConfigControllerPutApi, content); // send async za parametre

            return await response.ReadContentAsync<bool>();
        }


        // Types
        public async Task<IEnumerable<DeviceType>> DeviceTypeGet(string jwtKey)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 
            var response = await httpClient.GetAsync(DeviceTypeGetApi); // send async za parametre

            return await response.ReadContentAsync<IEnumerable<DeviceType>>();
        }

        public async Task<IEnumerable<DeviceTypeRelay>> DeviceTypeRelayGet(string jwtKey)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 
            var response = await httpClient.GetAsync(DeviceTypeRelayGetApi); // send async za parametre

            return await response.ReadContentAsync<IEnumerable<DeviceTypeRelay>>();
        }

        public async Task<IEnumerable<DeviceTypeSensor>> DeviceTypeSensorGet(string jwtKey)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 
            var response = await httpClient.GetAsync(DeviceTypeSensorGetApi); // send async za parametre

            return await response.ReadContentAsync<IEnumerable<DeviceTypeSensor>>();
        }

        public async Task<IEnumerable<DeviceTypeService>> DeviceTypeServiceGet(string jwtKey)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 
            var response = await httpClient.GetAsync(DeviceTypeServiceGetApi); // send async za parametre

            return await response.ReadContentAsync<IEnumerable<DeviceTypeService>>();
        }


        #endregion


        #region SensorData
        public async Task<string> SensorDataGet(string jwtKey, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 

            var query = HttpUtility.ParseQueryString(string.Empty);
            query["deviceID"] = deviceID.ToString();
            query["timeRange"] = timeRange.ToString();
            query["timeMDMY"] = timeMDMY.ToString();
            query["buildReport"] = buildReport.ToString();


            string queryString = SensorDataGetApi + query.ToString();

            var response = await httpClient.GetAsync(queryString);

            return await response.Content.ReadAsStringAsync();
        }
        public async Task<IEnumerable<SensorDataReport>> SensorDataReportGet(string? jwtKey, int? idDevice, int? iDSensorDataReport, int? getData)
        {
            //SensorDataReportGetApi
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 

            var query = HttpUtility.ParseQueryString(string.Empty);
            if (idDevice != null) { query["idDevice"] = idDevice.ToString(); }
            if (iDSensorDataReport != null) { query["iDSensorDataReport"] = iDSensorDataReport.ToString(); }
            if (getData != null) { query["getData"] = getData.ToString(); }

            string queryString = SensorDataReportGetApi + query.ToString();

            var response = await httpClient.GetAsync(queryString);

            return await response.ReadContentAsync<IEnumerable<SensorDataReport>>();
        }

        #endregion

        #region User 

        public async Task<UserLoginResult>? UserLogin(UserLogin userLogin)
        {

            string? jsonData = JsonConvert.SerializeObject(userLogin);
            HttpContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(UserLoginPostApi, content); // send async za parametre
            // ovo radi, ali imamo problem da se ne refresha.
            return await response.ReadContentAsync<UserLoginResult>();
        }


        public async Task<bool> UserAdd(string? jwtKey, UserAdd? userAdd)
        {
            
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 

            string? jsonData = JsonConvert.SerializeObject(userAdd);
            HttpContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(UserPostApi, content); // send async za parametre
            // ovo radi, ali imamo problem da se ne refresha.
            return await response.ReadContentAsync<bool>();
        }

        public async Task<bool> UserDelete(string jwtKey, int? idUser)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 
            var query = HttpUtility.ParseQueryString(string.Empty);
            if (idUser != null) { query["idUser"] = idUser.ToString(); }
            string queryString = UserDeleteApi + query.ToString();

            var response = await httpClient.DeleteAsync(queryString); // send async za parametre


            return await response.ReadContentAsync<bool>();
        }

        public async Task<User> UserGet(string? jwtKey, int? idUser, string? email, string? username)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 

            var query = HttpUtility.ParseQueryString(string.Empty);
            if (idUser != null) { query["idUser"] = idUser.ToString(); }
            if (email != null) { query["email"] = email.ToString(); }
            if (username != null) { query["username"] = username.ToString(); }

            string queryString = UserGetApi + query.ToString();

            var response = await httpClient.GetAsync(queryString);

            return await response.ReadContentAsync<User>();
        }
       
        public async Task<IEnumerable<User>> UsersGet(string jwtKey)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 
            var response = await httpClient.GetAsync(UsersGetApi); // send async za parametre

            return await response.ReadContentAsync<IEnumerable<User>>();
        }

        public async Task<bool> UserUpdate (string jwtKey, UserUpdate userUpdate)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 

            string? jsonData = JsonConvert.SerializeObject(userUpdate);
            HttpContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var response = await httpClient.PutAsync(UserPutApi, content); // send async za parametre

            return await response.ReadContentAsync<bool>();
        }
        public async Task<IEnumerable<UserRole>> UserRoleGet(string jwtKey)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 
            var response = await httpClient.GetAsync(UserRoleGetApi); // send async za parametre

            return await response.ReadContentAsync<IEnumerable<UserRole>>();
        }
        #endregion

        #region Group 

        public async Task <IEnumerable<UserGroup>> UserGroupsGet(string jwtKey)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 
            var response = await httpClient.GetAsync(UsersGroupsGetApi); // send async za parametre

            return await response.ReadContentAsync<IEnumerable<UserGroup>>();
        }

        public async Task<UserGroup> UserGroupGet(string jwtKey, int idUserGroup)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 

            var query = HttpUtility.ParseQueryString(string.Empty);
            query["idUserGroup"] = idUserGroup.ToString(); 

            string queryString = UsersGroupGetApi + query.ToString();

            var response = await httpClient.GetAsync(queryString);

            return await response.ReadContentAsync<UserGroup>();
        }

        public async Task<bool> UserGroupAdd(string jwtKey, UserGroup userGroup)
        {

            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 

            string? jsonData = JsonConvert.SerializeObject(userGroup);
            HttpContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(UsersGroupPostApi, content); // send async za parametre
            // ovo radi, ali imamo problem da se ne refresha.
            return await response.ReadContentAsync<bool>();

        }

        public async Task<bool> UserGroupDelete(string jwtKey, int? idUserGroup)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtKey); // add header value, work 
            var query = HttpUtility.ParseQueryString(string.Empty);
            if (idUserGroup != null) { query["idUserGroup"] = idUserGroup.ToString(); }
            string queryString = UsersGroupDeleteApi + query.ToString();

            var response = await httpClient.DeleteAsync(queryString); // send async za parametre


            return await response.ReadContentAsync<bool>();
        }






        #endregion

    }
}
