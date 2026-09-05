using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using api.Models;
using api.Utils;

namespace Agrumy.Api.Tests;

/// A request DTO enum without its own [JsonConverter] must still serialize as a plain number, matching what the API's [FromBody] binder expects - uses the exact <see cref="RefitConfig.Settings"/> Agrumy.Web sends with.
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

    [Fact]
    public async Task ExceptionFactory_SuccessStatus_ReturnsNull()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        Assert.Null(await RefitConfig.Settings.ExceptionFactory!(response));
    }

    [Fact]
    public async Task ExceptionFactory_401WithWwwAuthenticate_IsAuthChallenge()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("token expired") };
        response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer"));

        var ex = Assert.IsType<ApiException>(await RefitConfig.Settings.ExceptionFactory!(response));

        Assert.Equal(401, ex.StatusCode);
        Assert.True(ex.IsAuthChallenge);
    }

    [Fact]
    public async Task ExceptionFactory_401WithoutWwwAuthenticate_IsNotAuthChallenge()
    {
        // Same status code as a genuine auth-pipeline failure, but this is how an [Authorize]'d action's own StatusCode(401, ...) looks - no challenge header.
        using var response = new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("Wrong password") };

        var ex = Assert.IsType<ApiException>(await RefitConfig.Settings.ExceptionFactory!(response));

        Assert.Equal(401, ex.StatusCode);
        Assert.False(ex.IsAuthChallenge);
    }
}
