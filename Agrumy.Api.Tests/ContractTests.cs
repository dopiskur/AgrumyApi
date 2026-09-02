using System.Text.Json;
using System.Text.Json.Nodes;
using api.Models;
using Json.Schema;

namespace Agrumy.Api.Tests;

/// <summary>
/// Enforces the firmware &lt;-&gt; API contract in <c>contracts/device-api/*.schema.json</c>.
///
/// The schemas were derived from the real behaviour of both code bases (see that folder's
/// README). These tests serialize the C# models exactly the way ASP.NET Core MVC does
/// (System.Text.Json Web defaults =&gt; camelCase, nulls kept) and validate the output against
/// the schemas, so a rename/removal/casing change in <see cref="DeviceConfig"/> and friends -
/// which the C# compiler happily accepts - fails here instead of on a device in the field.
///
/// No database: the response fixtures are constructed the same way
/// <c>DeviceApiController.BuildDeviceConfigAsync()</c> / <c>ReqAuth()</c> build them.
/// </summary>
public class ContractTests
{
    // Matches Microsoft.AspNetCore.Mvc's internal default (JsonSerializerDefaults.Web):
    // camelCase, case-insensitive, NumberHandling.AllowReadingFromString, nulls NOT ignored.
    private static readonly JsonSerializerOptions Mvc = new(JsonSerializerDefaults.Web);

    private static readonly string SchemaDir =
        Path.Combine(AppContext.BaseDirectory, "contracts", "device-api");

    private static readonly string[] ExpectedSchemaFiles =
    {
        "register.request.schema.json",
        "register.response.schema.json",
        "authenticate.request.schema.json",
        "authenticate.response.schema.json",
        "config.request.schema.json",
        "config.response.schema.json",
        "sensordata.request.schema.json",
    };

    private static JsonSchema Load(string file) =>
        JsonSchema.FromText(File.ReadAllText(Path.Combine(SchemaDir, file)));

    private static void AssertValid(string schemaFile, string json)
    {
        var result = Load(schemaFile).Evaluate(
            JsonNode.Parse(json),
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        if (!result.IsValid)
        {
            var details = result.Details
                .Where(d => d.HasErrors)
                .SelectMany(d => d.Errors!.Select(e => $"  {d.InstanceLocation} : {e.Key} -> {e.Value}"));
            Assert.Fail($"{schemaFile} rejected the payload:\n{string.Join("\n", details)}\n\npayload:\n{json}");
        }
    }

    // ---- housekeeping ------------------------------------------------------------------

    [Fact]
    public void AllSevenSchemaFilesArePresentAndParseAsSchemas()
    {
        foreach (var f in ExpectedSchemaFiles)
        {
            var path = Path.Combine(SchemaDir, f);
            Assert.True(File.Exists(path), $"missing contract schema: {f} (expected at {path})");
            _ = Load(f); // throws if not a parseable schema
        }

        var onDisk = Directory.GetFiles(SchemaDir, "*.schema.json").Select(Path.GetFileName).OrderBy(x => x);
        Assert.Equal(ExpectedSchemaFiles.OrderBy(x => x), onDisk);
    }

    [Fact]
    public void RegisterAndConfigResponseSchemasAreIdentical()
    {
        // They are both the serialized DeviceConfig; the READMEs say to keep them in sync.
        static string Body(string f)
        {
            var n = JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaDir, f)))!.AsObject();
            n.Remove("$id");
            n.Remove("title");
            return n.ToJsonString();
        }
        Assert.Equal(Body("config.response.schema.json"), Body("register.response.schema.json"));
    }

    // ---- responses (server -> firmware) ----------------------------------------------

    private static DeviceConfig FullConfig() => new()
    {
        ConfigVersion = 66,
        TenantID = 0,
        deviceID = 1000038,
        DeviceUnitID = 0,
        DeviceUnitZoneID = 0,
        DeviceTypeServiceID = 1,
        ApiId = "4527ae5d-0cd7-4dbd-9ada-6994450ed887",
        ApiKey = "1967b2e9-e5bf-432a-bccb-8c96cb2b5821",
        ServicePoint = "api.agrumy.com",
        ServicePublicKey = null,
        DeviceSensorEnabled = true,
        DeviceControllerEnabled = true,
        BatteryEnabled = false,
        Debug = true,
        Reboot = false,
        Reset = false,
        FirmwareUpdate = true,
        FirmwareVersion = "0.1.2",
        FirmwareUrl = "https://cdn.agrumy.com/firmware/esp32/0.1.2.bin",
        Enabled = true,
        DeviceConfigSensor = new DeviceConfigSensor { IDDeviceConfigSensor = 100029 },
        DeviceConfigController = new DeviceConfigController { IDDeviceConfigController = 100029 },
    };

    [Theory]
    [InlineData("config.response.schema.json")]
    [InlineData("register.response.schema.json")]
    public void DeviceConfig_FullyPopulated_MatchesResponseSchema(string schema)
    {
        AssertValid(schema, JsonSerializer.Serialize(FullConfig(), Mvc));
    }

    [Theory]
    [InlineData("config.response.schema.json")]
    [InlineData("register.response.schema.json")]
    public void DeviceConfig_SensorAndControllerDisabled_NestedAreNull_MatchesResponseSchema(string schema)
    {
        // BuildDeviceConfigAsync leaves DeviceConfigSensor/Controller null when the *Enabled
        // flags are false; the live response confirmed the keys are still emitted as null.
        var cfg = FullConfig();
        cfg.DeviceSensorEnabled = false;
        cfg.DeviceControllerEnabled = false;
        cfg.DeviceConfigSensor = null;
        cfg.DeviceConfigController = null;

        var json = JsonSerializer.Serialize(cfg, Mvc);
        Assert.Contains("\"deviceConfigSensor\":null", json);
        AssertValid(schema, json);
    }

    [Fact]
    public void DeviceConfig_DefaultInstance_MatchesResponseSchema()
    {
        // Guards the "every property still serializes under its expected camelCase key" invariant.
        AssertValid("config.response.schema.json", JsonSerializer.Serialize(new DeviceConfig(), Mvc));
    }

    [Theory]
    [InlineData("config.response.schema.json")]
    [InlineData("register.response.schema.json")]
    public void DeviceConfig_WithScheduleSlots_MatchesResponseSchema(string schema)
    {
        // Roadmap #115: exercises the deviceScheduleSlot definition itself, not just the "always
        // empty array" default every other test here happens to send.
        var cfg = FullConfig();
        cfg.DeviceConfigController!.VentilationSchedule =
        [
            new DeviceScheduleSlot { DaysOfWeek = 0b0111110, Start = 21600, Duration = 1800 },  // Mon-Fri 06:00-06:30
            new DeviceScheduleSlot { DaysOfWeek = 0b0111110, Start = 50400, Duration = 900 },    // Mon-Fri 14:00-14:15
        ];

        AssertValid(schema, JsonSerializer.Serialize(cfg, Mvc));
    }

    [Fact]
    public void DeviceAuthentication_MatchesResponseSchema()
    {
        var body = new DeviceAuthentication { apiAuth = Guid.NewGuid().ToString() };
        AssertValid("authenticate.response.schema.json", JsonSerializer.Serialize(body, Mvc));
    }

    // ---- requests (firmware -> server) ----------------------------------------------
    // Payloads are shaped exactly as the firmware sends them (see AgrumyDevice/src/Controller).

    [Fact]
    public void RegisterRequest_FirmwareShapedPayload_MatchesSchemaAndBinds()
    {
        // registerDevice(): macAddress/email/devicePin/serviceType, devicePin as a STRING
        // (6-char alphanumeric since roadmap #70).
        const string payload =
            """{"macAddress":"240AC4040AF8","email":"admin@agrumy.local","devicePin":"AB23CD","serviceType":1}""";

        AssertValid("register.request.schema.json", payload);

        var bound = JsonSerializer.Deserialize<DeviceRegistration>(payload, Mvc)!;
        Assert.Equal("240AC4040AF8", bound.MacAddress);
        Assert.Equal("AB23CD", bound.DevicePin);
        Assert.Equal(1, bound.ServiceType);
    }

    [Fact]
    public void ConfigRequest_FirmwareShapedPayload_MatchesSchemaAndBinds()
    {
        // apiConfig(): PascalCase keys, ConfigVersion sent as a STRING; the diagnostics fields
        // (roadmap #7) ride along as JSON numbers/string.
        const string payload =
            """{"ConfigVersion":"66","Uptime":3661,"Rssi":-67,"FreeHeap":153212,"FirmwareVersion":"0.1.2","Board":"esp32dev"}""";

        AssertValid("config.request.schema.json", payload);

        var bound = JsonSerializer.Deserialize<DeviceConfigPoll>(payload, Mvc)!;
        Assert.Equal(66, bound.ConfigVersion);
        Assert.Equal(3661, bound.Uptime);
        Assert.Equal(-67, bound.Rssi);
        Assert.Equal(153212, bound.FreeHeap);
        Assert.Equal("0.1.2", bound.FirmwareVersion);
        Assert.Equal("esp32dev", bound.Board); // roadmap #94
    }

    [Fact]
    public void AuthenticateRequest_EmptyBody_MatchesSchema()
    {
        // apiAuthenticate() sends an empty ArduinoJson doc -> "null"; "{}" is also allowed.
        AssertValid("authenticate.request.schema.json", "null");
        AssertValid("authenticate.request.schema.json", "{}");
    }

    [Fact]
    public void SensorDataRequest_FirmwareShapedPayload_MatchesSchema()
    {
        // buildSensorDataPayload(): an ARRAY; measurements are strings or null.
        const string payload =
            """
            [
              {
                "deviceID": 1000038, "tenantID": 0, "deviceUnitID": 0, "deviceUnitZoneID": 0,
                "temperature": "26.13", "soilTemperature": null, "humidity": "47.5", "battery": null,
                "moisture": null, "light": "2", "co2": "408", "tvoc": null, "barometer": "100727.82",
                "liquidPH": null, "rainLevel": null, "waterLevel": null, "wind": null,
                "dateCreated": "2026-08-29 09:50:00"
              }
            ]
            """;

        AssertValid("sensordata.request.schema.json", payload);
    }
}
