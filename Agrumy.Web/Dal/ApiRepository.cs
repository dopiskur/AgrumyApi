using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Web;
using api.Dal.Interface;
using api.Models;
using api.Utils;

namespace api.Dal
{
    /// <summary>
    /// HTTP-backed <see cref="IApi"/> - the admin UI's only way to reach Agrumy.Api.
    ///
    /// The <see cref="HttpClient"/> is the one supplied by <c>IHttpClientFactory</c>
    /// (<c>AddHttpClient&lt;IApi, ApiRepository&gt;</c> in Program.cs); its <c>BaseAddress</c> is
    /// already set from <c>WebView:ApiService</c>. The caller's JWT is attached per request via a
    /// fresh <see cref="HttpRequestMessage"/> - never on <c>DefaultRequestHeaders</c>, which on a
    /// shared client would let concurrent requests from different users overwrite each other's
    /// bearer token.
    /// </summary>
    public class ApiRepository : IApi
    {
        private readonly HttpClient _client;

        public ApiRepository(HttpClient client) =>
            _client = client ?? throw new ArgumentNullException(nameof(client));

        // ---- endpoints -------------------------------------------------------

        private const string UserLoginApi = "/api/User/Login";
        private const string UsersGetApi = "/api/User/All";
        private const string UserApi = "/api/User";
        private const string UserRoleGetApi = "/api/User/Roles";
        private const string UserGroupsGetApi = "/api/User/Group/All";
        private const string UserGroupApi = "/api/User/Group";
        private const string DevicesGetApi = "/api/Device/All";
        private const string DeviceApi = "/api/Device";
        private const string DeviceConfigSensorApi = "/api/Device/Sensor";
        private const string DeviceConfigControllerApi = "/api/Device/Controller";
        private const string DeviceTypeGetApi = "/api/Device/Type";
        private const string DeviceTypeRelayGetApi = "/api/Device/TypeRelay";
        private const string DeviceTypeSensorGetApi = "/api/Device/TypeSensor";
        private const string DeviceTypeServiceGetApi = "/api/Device/TypeService";
        private const string SensorDataGetApi = "/api/SensorData";
        private const string SensorDataReportGetApi = "/api/SensorData/Report";

        // ---- request helpers ----------------------------------------------

        private static HttpRequestMessage Request(HttpMethod method, string url, string? jwt, object? body = null)
        {
            var request = new HttpRequestMessage(method, url);
            if (!string.IsNullOrEmpty(jwt))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            }
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: HttpClientExtensions.Json);
            }
            return request;
        }

        private async Task<T> Send<T>(HttpMethod method, string url, string? jwt, object? body = null)
        {
            using var response = await _client.SendAsync(Request(method, url, jwt, body)).ConfigureAwait(false);
            return await response.ReadJsonAsync<T>().ConfigureAwait(false);
        }

        private static string WithQuery(string path, params (string key, object? value)[] parameters)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            foreach (var (key, value) in parameters)
            {
                if (value is not null)
                {
                    query[key] = value.ToString();
                }
            }
            string qs = query.ToString() ?? "";
            return qs.Length == 0 ? path : $"{path}?{qs}";
        }

        // ---- Device ----------------------------------------------------

        public Task<IEnumerable<Device>> DevicesGet(string jwtKey) =>
            Send<IEnumerable<Device>>(HttpMethod.Get, DevicesGetApi, jwtKey);

        public Task<Device> DeviceGet(string jwtKey, int? idDevice, string? apiId, string? macAddress) =>
            Send<Device>(HttpMethod.Get,
                WithQuery(DeviceApi, ("idDevice", idDevice), ("apiId", apiId), ("macAddress", macAddress)), jwtKey);

        public Task<bool> DeviceUpdate(string jwtKey, Device? device) =>
            Send<bool>(HttpMethod.Put, DeviceApi, jwtKey, device);

        public Task<bool> DeviceDelete(string jwtKey, int? idDevice) =>
            Send<bool>(HttpMethod.Delete, WithQuery(DeviceApi, ("idDevice", idDevice)), jwtKey);

        public Task<DeviceConfigSensor> DeviceConfigSensorGet(string jwtKey, int? deviceConfigSensorID) =>
            Send<DeviceConfigSensor>(HttpMethod.Get,
                WithQuery(DeviceConfigSensorApi, ("deviceConfigSensorID", deviceConfigSensorID)), jwtKey);

        public Task<DeviceConfigController> DeviceConfigControllerGet(string jwtKey, int? deviceConfigControllerID) =>
            Send<DeviceConfigController>(HttpMethod.Get,
                WithQuery(DeviceConfigControllerApi, ("deviceConfigControllerID", deviceConfigControllerID)), jwtKey);

        public Task<bool> DeviceConfigSensorUpdate(string jwtKey, DeviceUpdate deviceUpdate) =>
            Send<bool>(HttpMethod.Put, DeviceConfigSensorApi, jwtKey, deviceUpdate);

        public Task<bool> DeviceConfigControllerUpdate(string jwtKey, DeviceUpdate deviceUpdate) =>
            Send<bool>(HttpMethod.Put, DeviceConfigControllerApi, jwtKey, deviceUpdate);

        public Task<IEnumerable<DeviceType>> DeviceTypeGet(string jwtKey) =>
            Send<IEnumerable<DeviceType>>(HttpMethod.Get, DeviceTypeGetApi, jwtKey);

        public Task<IEnumerable<DeviceTypeRelay>> DeviceTypeRelayGet(string jwtKey) =>
            Send<IEnumerable<DeviceTypeRelay>>(HttpMethod.Get, DeviceTypeRelayGetApi, jwtKey);

        public Task<IEnumerable<DeviceTypeSensor>> DeviceTypeSensorGet(string jwtKey) =>
            Send<IEnumerable<DeviceTypeSensor>>(HttpMethod.Get, DeviceTypeSensorGetApi, jwtKey);

        public Task<IEnumerable<DeviceTypeService>> DeviceTypeServiceGet(string jwtKey) =>
            Send<IEnumerable<DeviceTypeService>>(HttpMethod.Get, DeviceTypeServiceGetApi, jwtKey);

        // ---- SensorData ---------------------------------------------

        public async Task<string> SensorDataGet(string jwtKey, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport)
        {
            string url = WithQuery(SensorDataGetApi,
                ("deviceID", deviceID), ("timeRange", timeRange), ("timeMDMY", timeMDMY), ("buildReport", buildReport));
            using var response = await _client.SendAsync(Request(HttpMethod.Get, url, jwtKey)).ConfigureAwait(false);
            return await response.ReadStringAsync().ConfigureAwait(false);
        }

        public Task<IEnumerable<SensorDataReport>> SensorDataReportGet(string? jwtKey, int? idDevice, int? iDSensorDataReport, int? getData) =>
            Send<IEnumerable<SensorDataReport>>(HttpMethod.Get,
                WithQuery(SensorDataReportGetApi,
                    ("idDevice", idDevice), ("iDSensorDataReport", iDSensorDataReport), ("getData", getData)), jwtKey);

        // ---- User ------------------------------------------------

        public Task<UserLoginResult?> UserLogin(UserLogin userLogin) =>
            Send<UserLoginResult?>(HttpMethod.Post, UserLoginApi, jwt: null, body: userLogin);

        public Task<bool> UserAdd(string jwtKey, UserAdd user) =>
            Send<bool>(HttpMethod.Post, UserApi, jwtKey, user);

        public Task<bool> UserUpdate(string jwtKey, UserUpdate userUpdate) =>
            Send<bool>(HttpMethod.Put, UserApi, jwtKey, userUpdate);

        public Task<bool> UserDelete(string jwtKey, int? idUser) =>
            Send<bool>(HttpMethod.Delete, WithQuery(UserApi, ("idUser", idUser)), jwtKey);

        public Task<User> UserGet(string? jwtKey, int? idUser, string? email, string? username) =>
            Send<User>(HttpMethod.Get,
                WithQuery(UserApi, ("idUser", idUser), ("email", email), ("username", username)), jwtKey);

        public Task<IEnumerable<User>> UsersGet(string jwtKey) =>
            Send<IEnumerable<User>>(HttpMethod.Get, UsersGetApi, jwtKey);

        public Task<IEnumerable<UserRole>> UserRoleGet(string jwtKey) =>
            Send<IEnumerable<UserRole>>(HttpMethod.Get, UserRoleGetApi, jwtKey);

        // ---- Group -----------------------------------------------

        public Task<IEnumerable<UserGroup>> UserGroupsGet(string jwtKey) =>
            Send<IEnumerable<UserGroup>>(HttpMethod.Get, UserGroupsGetApi, jwtKey);

        public Task<UserGroup> UserGroupGet(string jwtKey, int idUserGroup) =>
            Send<UserGroup>(HttpMethod.Get, WithQuery(UserGroupApi, ("idUserGroup", idUserGroup)), jwtKey);

        public Task<bool> UserGroupAdd(string jwtKey, UserGroup userGroup) =>
            Send<bool>(HttpMethod.Post, UserGroupApi, jwtKey, userGroup);

        public Task<bool> UserGroupDelete(string jwtKey, int? idUserGroup) =>
            Send<bool>(HttpMethod.Delete, WithQuery(UserGroupApi, ("idUserGroup", idUserGroup)), jwtKey);
    }
}
