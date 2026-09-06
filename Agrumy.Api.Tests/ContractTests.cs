using System.Text.Json;
using System.Text.Json.Nodes;
using api.Models;
using Json.Schema;

namespace Agrumy.Api.Tests;

/// Enforces the firmware &lt;-&gt; API contract in <c>contracts/device-api/*.schema.json</c>: serializes the C# models exactly the way ASP.NET Core MVC does and validates against the schemas, so a rename/casing change the compiler happily accepts fails here instead of on a device in the field.
public class ContractTests
{
    // Matches Microsoft.AspNetCore.Mvc's internal default (JsonSerializerDefaults.Web).
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
        "controllerdata.request.schema.json",
        "simulation.response.schema.json",
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


    [Fact]
    public void AllNineSchemaFilesArePresentAndParseAsSchemas()
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
        static string Body(string f)
        {
            var n = JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaDir, f)))!.AsObject();
            n.Remove("$id");
            n.Remove("title");
            return n.ToJsonString();
        }
        Assert.Equal(Body("config.response.schema.json"), Body("register.response.schema.json"));
    }


    private static DeviceConfig FullConfig() => new()
    {
        ConfigVersion = 66,
        TenantID = 0,
        deviceID = 1000038,
        DeviceFarmUnitID = 0,
        DeviceFarmUnitZoneID = 0,
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
        DeviceConfigController = new DeviceConfigController
        {
            IDDeviceConfigController = 100029,
            ManualOverrides =
            [
                new DeviceManualOverridePush { RelayFunction = RelayFunction.Heating, Mode = ManualOverrideMode.Target, ExpiresAtEpoch = 1893456000, TargetMetric = SensorMetric.Temperature, TargetThreshold = 22.0, TargetHysteresis = 1.0 },
            ],
        },
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
        // BuildDeviceConfigAsync leaves DeviceConfigSensor/Controller null when the *Enabled flags are false; the keys are still emitted as null, not omitted.
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
        AssertValid("config.response.schema.json", JsonSerializer.Serialize(new DeviceConfig(), Mvc));
    }

    [Theory]
    [InlineData("config.response.schema.json")]
    [InlineData("register.response.schema.json")]
    public void DeviceConfig_WithZoneRules_MatchesResponseSchema(string schema)
    {
        // Exercises all three deviceFarmUnitZoneRule/conditionConfig definitions, not just the "always empty rules array" default every other test here sends.
        var cfg = FullConfig();
        cfg.DeviceConfigController!.Rules =
        [
            new DeviceFarmUnitZoneRule
            {
                IDDeviceFarmUnitZoneRule = 1, RelayFunction = RelayFunction.WaterPump,
                Conditions = [new RuleCondition(ConditionType.Threshold, JsonSerializer.SerializeToNode(new ThresholdConditionConfig(10, 5), ConditionConfigJson.Options), null)],
            },
            new DeviceFarmUnitZoneRule
            {
                IDDeviceFarmUnitZoneRule = 2, RelayFunction = RelayFunction.Heating,
                Conditions = [new RuleCondition(ConditionType.Interval, JsonSerializer.SerializeToNode(new IntervalConditionConfig(3600, 300), ConditionConfigJson.Options), null)],
            },
            new DeviceFarmUnitZoneRule
            {
                IDDeviceFarmUnitZoneRule = 3, RelayFunction = RelayFunction.Light,
                // Two-condition AND group - Mon-Fri 06:00-06:30 AND a threshold, exercising the #212 conditions array (not just the single-condition case).
                Conditions =
                [
                    new RuleCondition(ConditionType.Schedule, JsonSerializer.SerializeToNode(new ScheduleConditionConfig(0b0111110, 21600, 1800), ConditionConfigJson.Options), null),
                    new RuleCondition(ConditionType.Threshold, JsonSerializer.SerializeToNode(new ThresholdConditionConfig(500, 50), ConditionConfigJson.Options), LogicalOperator.And),
                ],
            },
        ];

        AssertValid(schema, JsonSerializer.Serialize(cfg, Mvc));
    }

    [Fact]
    public void DeviceAuthentication_MatchesResponseSchema()
    {
        var body = new DeviceAuthentication { apiAuth = Guid.NewGuid().ToString() };
        AssertValid("authenticate.response.schema.json", JsonSerializer.Serialize(body, Mvc));
    }

    // Payloads are shaped exactly as the firmware sends them (see AgrumyFirmware/src/Controller).

    [Fact]
    public void RegisterRequest_FirmwareShapedPayload_MatchesSchemaAndBinds()
    {
        // registerDevice(): macAddress/email/devicePin/serviceType, devicePin as a 6-char alphanumeric STRING.
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
        // apiConfig(): PascalCase keys, ConfigVersion sent as a STRING; the diagnostics fields ride along as JSON numbers/string.
        const string payload =
            """{"ConfigVersion":"66","Uptime":3661,"Rssi":-67,"FreeHeap":153212,"FirmwareVersion":"0.1.2","Board":"esp32dev","Kit":""}""";

        AssertValid("config.request.schema.json", payload);

        var bound = JsonSerializer.Deserialize<DeviceConfigPoll>(payload, Mvc)!;
        Assert.Equal(66, bound.ConfigVersion);
        Assert.Equal(3661, bound.Uptime);
        Assert.Equal(-67, bound.Rssi);
        Assert.Equal(153212, bound.FreeHeap);
        Assert.Equal("0.1.2", bound.FirmwareVersion);
        Assert.Equal("esp32dev", bound.Board);
        Assert.Equal("", bound.Kit); // empty on a generic (non-kit) environment
    }

    [Fact]
    public void ConfigRequest_KitShapedPayload_MatchesSchemaAndBinds()
    {
        const string payload =
            """{"ConfigVersion":"66","Uptime":3661,"Rssi":-67,"FreeHeap":153212,"FirmwareVersion":"0.1.2","Board":"kc868-a6","Kit":"KC868-A6"}""";

        AssertValid("config.request.schema.json", payload);

        var bound = JsonSerializer.Deserialize<DeviceConfigPoll>(payload, Mvc)!;
        Assert.Equal("kc868-a6", bound.Board);
        Assert.Equal("KC868-A6", bound.Kit);
    }

    [Fact]
    public void AuthenticateRequest_EmptyBody_MatchesSchema()
    {
        AssertValid("authenticate.request.schema.json", "null");
        AssertValid("authenticate.request.schema.json", "{}");
    }

    // Pre-#326 firmware (String+atof SensorData) - still accepted since not every device in the field is on the new firmware yet.
    [Fact]
    public void SensorDataRequest_LegacyStringShapedPayload_MatchesSchema()
    {
        const string payload =
            """
            [
              {
                "deviceID": 1000038, "tenantID": 0, "deviceFarmUnitID": 0, "deviceFarmUnitZoneID": 0,
                "temperature": "26.13", "soilTemperature": null, "humidity": "47.5", "battery": null,
                "moisture": null, "light": "2", "co2": "408", "tvoc": null, "barometer": "100727.82",
                "liquidPH": null, "rainLevel": null, "waterLevel": null, "wind": null,
                "dateCreated": "2026-08-29 09:50:00"
              }
            ]
            """;

        AssertValid("sensordata.request.schema.json", payload);
    }

    // Roadmap #326: SensorData moved from Arduino String+atof to double/NaN, so a real reading now serializes as a JSON number instead of a numeric string.
    [Fact]
    public void SensorDataRequest_FirmwareShapedPayload_MatchesSchema()
    {
        const string payload =
            """
            [
              {
                "deviceID": 1000038, "tenantID": 0, "deviceFarmUnitID": 0, "deviceFarmUnitZoneID": 0,
                "temperature": 26.13, "soilTemperature": null, "humidity": 47.5, "battery": null,
                "moisture": null, "light": 2, "co2": 408, "tvoc": null, "barometer": 100727.82,
                "liquidPH": null, "rainLevel": null, "waterLevel": null, "wind": null,
                "dateCreated": "2026-08-29 09:50:00"
              }
            ]
            """;

        AssertValid("sensordata.request.schema.json", payload);
    }

    // Real C# serialization of the push DTO, not a hand-written literal - catches a casing/shape drift the compiler would happily accept.
    [Fact]
    public void ControllerDataRequest_RealSerialization_MatchesSchema()
    {
        var entries = new List<ControllerDataPush>
        {
            new() { RelayFunction = RelayFunction.Heating, IsOn = true, DateCreated = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc) },
            new() { RelayFunction = RelayFunction.WaterPump, IsOn = false, DateCreated = null },
        };

        AssertValid("controllerdata.request.schema.json", JsonSerializer.Serialize(entries, Mvc));
    }

    // Only reachable when Enabled is true - the server answers 204 otherwise (DeviceApiController.DeviceSimulationPoll), so this is the only shape that ever reaches the wire.
    [Fact]
    public void SimulationResponse_RealSerialization_MatchesSchema()
    {
        var sim = new DeviceSimulation { Enabled = true, Temperature = 26.5, Humidity = 55.0, Co2 = 800 };
        AssertValid("simulation.response.schema.json", JsonSerializer.Serialize(sim, Mvc));
    }
}
