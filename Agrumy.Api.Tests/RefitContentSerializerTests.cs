using System.Text.Json;
using System.Text.Json.Nodes;
using api.Models;
using api.Utils;

namespace Agrumy.Api.Tests;

/// <summary>Refit's default ContentSerializer silently registers a global JsonStringEnumConverter, so a request DTO enum without its own [JsonConverter] (RelayFunction, ConditionType, CommandActionType/CommandTargetType) serialized as a camelCase string - but the API's [FromBody] binder expects these as plain numeric enums, so every such call 400'd. Serializes with the exact <see cref="RefitConfig.Settings"/> Agrumy.Web uses and deserializes with the same Web-defaults options ContractTests.cs uses, so a regression here fails the same way the live bug did.</summary>
public class RefitContentSerializerTests
{
    private static readonly JsonSerializerOptions Mvc = new(JsonSerializerDefaults.Web);

    private static string SerializeAsWebWould<T>(T value)
    {
        using var content = RefitConfig.Settings.ContentSerializer.ToHttpContent(value);
        return content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public void DeviceUnitZoneRule_RelayFunctionAndConditionType_SerializeAsNumbers()
    {
        var rule = new DeviceUnitZoneRule
        {
            DeviceUnitZoneID = 5,
            RelayFunction = RelayFunction.Ventilation,
            ConditionType = ConditionType.Threshold,
            ConditionConfig = JsonSerializer.SerializeToNode(new ThresholdConditionConfig(10, 2), ConditionConfigJson.Options),
        };

        string wire = SerializeAsWebWould(rule);

        var parsed = JsonSerializer.Deserialize<DeviceUnitZoneRule>(wire, Mvc);
        Assert.NotNull(parsed);
        Assert.Equal(RelayFunction.Ventilation, parsed!.RelayFunction);
        Assert.Equal(ConditionType.Threshold, parsed.ConditionType);
    }

    [Fact]
    public void IssueCommandRequest_TargetAndActionType_SerializeAsNumbers()
    {
        var request = new IssueCommandRequest
        {
            TargetType = CommandTargetType.Zone,
            TargetId = 7,
            ActionType = CommandActionType.ForceOTA,
        };

        string wire = SerializeAsWebWould(request);

        var parsed = JsonSerializer.Deserialize<IssueCommandRequest>(wire, Mvc);
        Assert.NotNull(parsed);
        Assert.Equal(CommandTargetType.Zone, parsed!.TargetType);
        Assert.Equal(CommandActionType.ForceOTA, parsed.ActionType);
    }
}
